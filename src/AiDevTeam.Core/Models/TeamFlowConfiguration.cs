namespace AiDevTeam.Core.Models;

public class TeamFlowConfiguration
{
    public string Name { get; set; } = "Default Flow";
    public string Description { get; set; } = string.Empty;
    public List<FlowStep> Steps { get; set; } = [];
    public string? DefaultCommunicationStyle { get; set; } = "professional";

    /// <summary>
    /// Maximum number of review-code-review cycles before forcing a decision.
    /// </summary>
    public int MaxReviewCycles { get; set; } = 3;

    /// <summary>
    /// Maximum number of retries per step on failure.
    /// </summary>
    public int MaxRetriesPerStep { get; set; } = 2;

    /// <summary>
    /// Whether the orchestrator should always ask the user before starting implementation.
    /// </summary>
    public bool RequireUserApprovalBeforeCoding { get; set; } = true;

    /// <summary>
    /// Whether to run tests after implementation (requires a Tester step in the flow).
    /// </summary>
    public bool RunTestsAfterReview { get; set; } = true;
}
