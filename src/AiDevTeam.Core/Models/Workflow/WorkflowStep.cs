namespace AiDevTeam.Core.Models.Workflow;

/// <summary>
/// A single executed step in the workflow.
/// Each step records: who ran, what input was given, what output came back,
/// and what artifacts were produced. Fully inspectable.
/// </summary>
public class WorkflowStep
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public int StepIndex { get; set; }
    public string AgentRole { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;

    public WorkflowStepStatus Status { get; set; } = WorkflowStepStatus.Pending;

    /// <summary>Path to the JSON input file sent to the agent.</summary>
    public string? InputJsonPath { get; set; }

    /// <summary>Path to the JSON output file received from the agent.</summary>
    public string? OutputJsonPath { get; set; }

    /// <summary>The chat message ID for the human-readable summary in conversation.</summary>
    public string? ChatMessageId { get; set; }

    /// <summary>The AgentRun ID for linking to execution details.</summary>
    public string? AgentRunId { get; set; }

    /// <summary>Human-readable summary of the step result (used for delegation context to next agent).</summary>
    public string? Summary { get; set; }

    public List<string> ProducedArtifactIds { get; set; } = [];

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum WorkflowStepStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped
}
