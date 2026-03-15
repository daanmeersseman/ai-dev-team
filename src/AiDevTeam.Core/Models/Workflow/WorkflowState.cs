namespace AiDevTeam.Core.Models.Workflow;

/// <summary>
/// Deterministic workflow states for a task lifecycle.
/// Transitions are enforced by WorkflowStateMachine — not by convention.
/// </summary>
public enum WorkflowState
{
    Created,
    Analyzing,
    WaitingForInput,
    Planned,
    Coding,
    Reviewing,
    ChangesRequested,
    Testing,
    ReadyToMerge,
    Blocked,
    Failed,
    Completed
}
