using AiDevTeam.Core.Contracts;
using AiDevTeam.Core.Models.Workflow;

namespace AiDevTeam.Core.Interfaces;

/// <summary>
/// Deterministic state machine for workflow transitions.
/// All transitions are explicitly defined — no implicit state changes.
/// </summary>
public interface IWorkflowStateMachine
{
    bool TryTransition(WorkflowState from, WorkflowState to, out string? reason);
    IReadOnlyList<WorkflowState> GetAllowedTransitions(WorkflowState from);
    bool IsTransitionAllowed(WorkflowState from, WorkflowState to);
    WorkflowState DetermineNextState(WorkflowState current, WorkflowStepOutcome outcome);
}

/// <summary>
/// The outcome of a workflow step, used by the state machine to decide the next state.
/// </summary>
public class WorkflowStepOutcome
{
    public bool Success { get; set; }
    public string AgentRole { get; set; } = string.Empty;
    public bool NeedsUserInput { get; set; }
    public bool HasPlan { get; set; }
    public ReviewDecision? ReviewDecision { get; set; }
    public bool HasSuggestions { get; set; }
    public List<string> Suggestions { get; set; } = [];
    public TestDecision? TestDecision { get; set; }
    public string? ErrorMessage { get; set; }
}
