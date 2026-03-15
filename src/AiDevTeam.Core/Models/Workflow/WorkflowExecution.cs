using AiDevTeam.Core.Contracts;

namespace AiDevTeam.Core.Models.Workflow;

/// <summary>
/// The full workflow execution for a conversation/task.
/// This is the orchestrator's source of truth — persisted to disk as JSON.
/// </summary>
public class WorkflowExecution
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = string.Empty;
    public string? TeamId { get; set; }
    public string? FlowConfigurationName { get; set; }

    public WorkflowState CurrentState { get; set; } = WorkflowState.Created;
    public int CurrentStepIndex { get; set; }
    public int RetryCount { get; set; }
    public int ReviewCycleCount { get; set; }

    public List<WorkflowStep> Steps { get; set; } = [];
    public List<WorkflowDecision> Decisions { get; set; } = [];

    public List<PlannedStep> PlannedSteps { get; set; } = [];
    public List<string> PendingQuestions { get; set; } = [];
    public List<WorkflowOption> PendingOptions { get; set; } = [];

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
