namespace AiDevTeam.Core.Models;

public class Artifact
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public ArtifactType Type { get; set; } = ArtifactType.Markdown;
    public string? CreatedByAgent { get; set; }
    public string? LastModifiedByAgent { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum ArtifactType
{
    Markdown,
    Json,
    Code,
    Log,
    Patch,
    Image,
    Text,
    Other
}
