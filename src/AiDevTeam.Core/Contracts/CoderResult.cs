namespace AiDevTeam.Core.Contracts;

public class CoderResult
{
    public CoderStatus Status { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<ChangedFile> ChangedFiles { get; set; } = [];
    public List<string> ImplementedChanges { get; set; } = [];
    public List<string> UnresolvedItems { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public bool CanContinueToReview { get; set; } = true;
}

public enum CoderStatus
{
    Completed,
    PartiallyCompleted,
    Blocked,
    Failed
}
