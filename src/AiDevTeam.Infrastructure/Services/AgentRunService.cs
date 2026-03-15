using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using Microsoft.Extensions.Logging;

namespace AiDevTeam.Infrastructure.Services;

/// <summary>
/// Thin coordinator that delegates to focused sub-services.
/// Implements the public IAgentRunService contract without change.
/// </summary>
public class AgentRunService : IAgentRunService
{
    private readonly IAgentRoutingService _routing;
    private readonly IAgentExecutionService _execution;
    private readonly IAgentDefinitionService _agentService;
    private readonly IConversationService _conversationService;
    private readonly ITeamService _teamService;
    private readonly ILogger<AgentRunService> _logger;

    public AgentRunService(
        IAgentRoutingService routing,
        IAgentExecutionService execution,
        IAgentDefinitionService agentService,
        IConversationService conversationService,
        ITeamService teamService,
        ILogger<AgentRunService> logger)
    {
        _routing = routing;
        _execution = execution;
        _agentService = agentService;
        _conversationService = conversationService;
        _teamService = teamService;
        _logger = logger;
    }

    /// <summary>
    /// Main entry: every user message goes to the orchestrator first,
    /// UNLESS the message starts with an agent's first name — then dispatch directly.
    /// </summary>
    public async Task SendMessageAsync(string conversationId, string userMessage)
    {
        var agents = await GetConversationAgentsAsync(conversationId);

        // Direct agent mention — skip orchestrator hop
        var directTarget = _routing.TryMatchDirectMention(userMessage, agents);
        if (directTarget != null)
        {
            _logger.LogInformation("Direct mention detected — dispatching to {Agent}, skipping orchestrator", directTarget.Name);
            await _execution.StartRunAsync(conversationId, directTarget.Id, userMessage);
            return;
        }

        // Find orchestrator
        var orchestrator = agents.FirstOrDefault(a => a.Role == AgentRole.Orchestrator);
        if (orchestrator == null)
        {
            // No orchestrator — use first available agent
            var fallback = agents.FirstOrDefault(a => a.IsEnabled);
            if (fallback == null) return;
            await _execution.StartRunAsync(conversationId, fallback.Id, userMessage);
            return;
        }

        await _routing.StartOrchestratorRoutingAsync(conversationId, orchestrator, agents, userMessage);
    }

    public Task<AgentRun> StartRunAsync(string conversationId, string agentDefinitionId, string prompt)
        => _execution.StartRunAsync(conversationId, agentDefinitionId, prompt);

    public Task<AgentRun> ExecuteRunToCompletionAsync(string conversationId, string agentDefinitionId, string prompt, CancellationToken ct = default, bool skipChatMessage = false)
        => _execution.ExecuteRunToCompletionAsync(conversationId, agentDefinitionId, prompt, ct, skipChatMessage);

    public Task<AgentRun?> GetByIdAsync(string id)
        => _execution.GetByIdAsync(id);

    public Task<List<AgentRun>> GetByConversationAsync(string conversationId)
        => _execution.GetByConversationAsync(conversationId);

    public Task CancelRunAsync(string runId)
        => _execution.CancelRunAsync(runId);

    private async Task<List<AgentDefinition>> GetConversationAgentsAsync(string conversationId)
    {
        var allAgents = await _agentService.GetAllAsync();
        var conversation = await _conversationService.GetByIdAsync(conversationId);

        if (conversation?.TeamId != null)
        {
            var team = await _teamService.GetByIdAsync(conversation.TeamId);
            if (team != null)
                return allAgents.Where(a => team.AgentIds.Contains(a.Id) && a.IsEnabled).ToList();
        }

        return allAgents.Where(a => a.IsEnabled).ToList();
    }
}
