namespace AiDevTeam.Core.Models;

public class Message
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string SenderRole { get; set; } = string.Empty;
    public MessageType Type { get; set; } = MessageType.UserInstruction;
    public string Content { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public string? RelatedArtifactId { get; set; }
    public string? AgentRunId { get; set; }
    public List<string>? ContextBlockIds { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public enum MessageType
{
    UserInstruction,
    AgentThoughtSummary,
    Plan,
    StatusUpdate,
    Blocker,
    Review,
    TestResult,
    ArtifactCreated,
    ArtifactUpdated,
    CommandExecution,
    SystemEvent,
    PullRequestEvent,
    WorkflowDelegation,
    WorkflowQuestion,
    WorkflowOptions,
    WorkflowDecision,
    WorkflowStepComplete,
    WorkflowError,
    AgentResponse
}
