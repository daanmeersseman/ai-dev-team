namespace AiDevTeam.Core.Contracts;

public class StepSummary
{
    public string AgentName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Decision { get; set; }
}
