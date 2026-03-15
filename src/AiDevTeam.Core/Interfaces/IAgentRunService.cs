using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

public interface IAgentRunService
{
    /// <summary>
    /// Main entry point: user sends a message. The orchestrator routes it to the right agent.
    /// </summary>
    Task SendMessageAsync(string conversationId, string userMessage);

    /// <summary>
    /// Low-level: run a specific agent with a prompt (used internally for dispatch).
    /// Fire-and-forget — returns immediately.
    /// </summary>
    Task<AgentRun> StartRunAsync(string conversationId, string agentDefinitionId, string prompt);

    /// <summary>
    /// Executes a run and awaits its completion. Returns the finished AgentRun with output populated.
    /// Use for workflow engine steps that need the result before proceeding.
    /// </summary>
    Task<AgentRun> ExecuteRunToCompletionAsync(string conversationId, string agentDefinitionId, string prompt, CancellationToken ct = default, bool skipChatMessage = false);

    Task<AgentRun?> GetByIdAsync(string id);
    Task<List<AgentRun>> GetByConversationAsync(string conversationId);
    Task CancelRunAsync(string runId);
}
