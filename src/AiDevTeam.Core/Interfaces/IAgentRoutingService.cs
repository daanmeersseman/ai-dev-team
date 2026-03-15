using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

/// <summary>
/// Handles orchestrator routing: analyzing user messages, parsing routing directives,
/// matching agents by name, and managing follow-up routing after agent completion.
/// </summary>
public interface IAgentRoutingService
{
    Task StartOrchestratorRoutingAsync(string conversationId, AgentDefinition orchestrator, List<AgentDefinition> allAgents, string userMessage);
    Task RequestFollowUpRoutingAsync(string conversationId, string completedAgentId, string runId, string output, int currentChainDepth);
    Task AttemptFallbackRoutingAsync(string conversationId, string userMessage, List<AgentDefinition> agents);
    AgentDefinition? TryMatchDirectMention(string message, List<AgentDefinition> agents);
}
