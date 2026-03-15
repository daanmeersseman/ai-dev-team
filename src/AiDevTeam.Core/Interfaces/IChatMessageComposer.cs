using AiDevTeam.Core.Contracts;
using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

/// <summary>
/// Composes human-readable chat messages from structured workflow events.
/// </summary>
public interface IChatMessageComposer
{
    WorkflowChatMessage ComposeDelegation(AgentDefinition orchestrator, AgentDefinition target, FlowStep step, string taskTitle, string? summary = null);
    WorkflowChatMessage ComposeOptionsPresentation(AgentDefinition orchestrator, OrchestratorResult analysis);
    WorkflowChatMessage ComposeQuestions(AgentDefinition orchestrator, OrchestratorResult analysis);
    WorkflowChatMessage ComposePlanPresentation(AgentDefinition orchestrator, OrchestratorResult analysis);
    WorkflowChatMessage ComposeStepComplete(AgentDefinition agent, string summary, string? detailedJson = null);
    WorkflowChatMessage ComposeReviewResult(AgentDefinition reviewer, ReviewResult result);
    WorkflowChatMessage ComposeTestResult(AgentDefinition tester, TesterResult result);
    WorkflowChatMessage ComposeSuggestionsApproval(AgentDefinition orchestrator, AgentDefinition reviewer, AgentDefinition coder, List<string> suggestions);
    WorkflowChatMessage ComposeError(string sender, string senderRole, string error);
}
