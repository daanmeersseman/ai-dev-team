using AiDevTeam.Core.Contracts;
using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

/// <summary>
/// Builds prompts for agent execution. Separates prompt construction from workflow logic.
/// </summary>
public interface IPromptBuilder
{
    string BuildSystemPrompt(AgentDefinition agent);
    string BuildTaskPrompt(AgentTaskInput input, AgentDefinition agent);
    string BuildOutputFormatInstructions(AgentRole role);
}
