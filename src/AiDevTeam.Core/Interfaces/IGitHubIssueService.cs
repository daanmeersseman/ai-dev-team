namespace AiDevTeam.Core.Interfaces;

public interface IGitHubIssueService
{
    Task<GitHubIssueInfo?> FetchIssueAsync(string url);
    Task<string> AnalyzeIssueAsync(GitHubIssueInfo issue);
}

public class GitHubIssueInfo
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = [];
    public string? Assignee { get; set; }
    public string Repository { get; set; } = string.Empty;
    public int Number { get; set; }
}
