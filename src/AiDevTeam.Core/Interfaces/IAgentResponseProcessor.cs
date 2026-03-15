using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

/// <summary>
/// Processes agent responses: posts to chat, extracts artifacts, updates rolling context.
/// </summary>
public interface IAgentResponseProcessor
{
    Task ProcessAgentResponseAsync(string conversationId, AgentDefinition agent, string response, string runId);
    Task ExtractAndSaveCodeArtifactsAsync(string conversationId, AgentDefinition agent, string response, string runId);
    Task UpdateAgentContextAsync(string conversationId, AgentDefinition agent, string prompt, string response, bool success);
}
