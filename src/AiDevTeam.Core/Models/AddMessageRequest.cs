namespace AiDevTeam.Core.Models;

public class AddMessageRequest
{
    public required string ConversationId { get; init; }
    public required string Sender { get; init; }
    public required string SenderRole { get; init; }
    public required MessageType Type { get; init; }
    public required string Content { get; init; }
    public string? RelatedArtifactId { get; init; }
    public string? AgentRunId { get; init; }
    public string? MetadataJson { get; init; }
}
