namespace AiDevTeam.Core.Models;

public class FlowStep
{
    public int Order { get; set; }
    public string AgentRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? ReportsTo { get; set; }
    public string? Condition { get; set; }
    public bool IsOptional { get; set; }

    /// <summary>
    /// Short chat message template for when this step starts.
    /// Placeholders: {agent}, {previousAgent}, {summary}, {taskTitle}
    /// </summary>
    public string? ChatTemplate { get; set; }

    /// <summary>
    /// Which step to go to on success (by order number). Null = next in sequence.
    /// </summary>
    public int? NextStepOnSuccess { get; set; }

    /// <summary>
    /// Which step to go to when review requests changes (by order number).
    /// Only relevant for Reviewer steps.
    /// </summary>
    public int? NextStepOnChangesRequested { get; set; }

    /// <summary>
    /// Required provider capability for this step. If set, the engine will
    /// try to use a provider that has this capability.
    /// </summary>
    public ProviderCapability? RequiredCapability { get; set; }
}
