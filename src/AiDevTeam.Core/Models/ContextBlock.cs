namespace AiDevTeam.Core.Models;

/// <summary>
/// A named piece of detailed context attached to a chat message.
/// The chat message shows a human-friendly summary with a clickable reference;
/// clicking it reveals this full context block.
/// </summary>
public class ContextBlock
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
