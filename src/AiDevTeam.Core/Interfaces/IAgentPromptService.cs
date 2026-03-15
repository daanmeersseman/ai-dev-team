using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

/// <summary>
/// Builds system prompts and context-aware prompts for agent execution.
/// </summary>
public interface IAgentPromptService
{
    Task<string> BuildSystemPromptAsync(string conversationId, AgentDefinition agent, List<AgentDefinition> allAgents);
    Task<string> BuildContextAwarePromptAsync(string conversationId, AgentDefinition agent, string currentPrompt);
}
