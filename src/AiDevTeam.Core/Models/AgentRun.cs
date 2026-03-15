namespace AiDevTeam.Core.Models;

public class AgentRun
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = string.Empty;
    public string AgentDefinitionId { get; set; } = string.Empty;
    public string? AgentName { get; set; }
    public AgentRunStatus Status { get; set; } = AgentRunStatus.Queued;
    public string? InputPrompt { get; set; }
    public string? OutputText { get; set; }
    public string? ErrorText { get; set; }
    public int? ExitCode { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public int ChainDepth { get; set; }
}

public enum AgentRunStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled
}
