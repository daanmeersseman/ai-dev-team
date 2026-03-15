namespace AiDevTeam.Core.Contracts;

public class ReviewResult
{
    public ReviewDecision Decision { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<ReviewIssue> MustFix { get; set; } = [];
    public List<ReviewIssue> NiceToHave { get; set; } = [];
    public List<string> TestGaps { get; set; } = [];
    public List<string> SecurityConcerns { get; set; } = [];
    public List<string> Blockers { get; set; } = [];

    public bool CanMerge => Decision is ReviewDecision.Approved or ReviewDecision.ApprovedWithSuggestions;
}
