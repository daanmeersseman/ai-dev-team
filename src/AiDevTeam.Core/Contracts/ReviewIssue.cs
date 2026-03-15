namespace AiDevTeam.Core.Contracts;

public class ReviewIssue
{
    public string Description { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string? Suggestion { get; set; }
    public ReviewSeverity Severity { get; set; }
}

public enum ReviewSeverity
{
    Info,
    Warning,
    Error,
    Critical
}
