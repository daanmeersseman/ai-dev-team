namespace AiDevTeam.Core.Contracts;

public class ArtifactReference
{
    public string ArtifactId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
    public string? ContentPreview { get; set; }
}
