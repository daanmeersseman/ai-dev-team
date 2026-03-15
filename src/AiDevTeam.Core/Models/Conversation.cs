namespace AiDevTeam.Core.Models;

public class Conversation
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ConversationStatus Status { get; set; } = ConversationStatus.New;
    public string? Tags { get; set; }
    public string? TeamId { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Links to the active workflow execution, if one is running.
    /// </summary>
    public string? WorkflowExecutionId { get; set; }
}

public enum ConversationStatus
{
    New,
    InProgress,
    WaitingForInput,
    InReview,
    Completed,
    Failed,
    Blocked
}

public enum Priority
{
    Low,
    Medium,
    High,
    Critical
}
