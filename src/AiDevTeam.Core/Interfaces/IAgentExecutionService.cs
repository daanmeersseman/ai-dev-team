using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

/// <summary>
/// Manages agent run lifecycle: creating runs, executing via providers,
/// tracking active runs, and cancellation.
/// </summary>
public interface IAgentExecutionService
{
    /// <summary>
    /// Starts a run in the background (fire-and-forget). Returns immediately.
    /// Use for chat flow and parallel agent execution.
    /// </summary>
    Task<AgentRun> StartRunAsync(string conversationId, string agentDefinitionId, string prompt, int chainDepth = 0);

    /// <summary>
    /// Executes a run and awaits its completion. Returns the finished AgentRun with output populated.
    /// Use for workflow engine steps that need the result before proceeding.
    /// When skipChatMessage is true, the response processor will NOT post the raw output to chat
    /// (the caller is responsible for composing and posting structured chat messages).
    /// </summary>
    Task<AgentRun> ExecuteRunToCompletionAsync(string conversationId, string agentDefinitionId, string prompt, CancellationToken ct = default, bool skipChatMessage = false);

    Task CancelRunAsync(string runId);
    Task<AgentRun?> GetByIdAsync(string id);
    Task<List<AgentRun>> GetByConversationAsync(string conversationId);
}
