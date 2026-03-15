namespace AiDevTeam.Core.Contracts;

/// <summary>
/// Structured input sent to any agent during a workflow step.
/// Serialized to JSON and stored on disk for inspection.
/// </summary>
public class AgentTaskInput
{
    public string TaskId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string StepId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string? ContextSummary { get; set; }
    public List<string> Constraints { get; set; } = [];

    public string AssignedRole { get; set; } = string.Empty;
    public string AssignedAgentName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;

    public List<StepSummary> PreviousSteps { get; set; } = [];
    public List<ArtifactReference> RelevantArtifacts { get; set; } = [];
    public List<TeamMemberInfo> TeamMembers { get; set; } = [];
    public string? UserFeedback { get; set; }

    public string ExpectedOutputFormat { get; set; } = string.Empty;
}
