namespace AiDevTeam.Core.Models.Workflow;

/// <summary>
/// Records a decision made during workflow execution.
/// This is the decision log — every state transition and routing decision is tracked.
/// </summary>
public class WorkflowDecision
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string DecisionMaker { get; set; } = string.Empty;
    public WorkflowDecisionType Type { get; set; }
    public string Reason { get; set; } = string.Empty;
    public WorkflowState? FromState { get; set; }
    public WorkflowState? ToState { get; set; }
    public string? DataJson { get; set; }
}

public enum WorkflowDecisionType
{
    StateTransition,
    AgentRouting,
    RetryRequested,
    UserIntervention,
    OptionSelected,
    QuestionAnswered,
    ErrorRecovery,
    StepSkipped,
    WorkflowCompleted,
    WorkflowFailed
}
