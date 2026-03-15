namespace AiDevTeam.Core.Contracts;

public class PlannedStep
{
    public int Order { get; set; }
    public string AgentRole { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsOptional { get; set; }
}
