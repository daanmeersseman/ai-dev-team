namespace AiDevTeam.Core.Models.Workflow;

public class WorkflowOption
{
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Recommendation { get; set; }
}
