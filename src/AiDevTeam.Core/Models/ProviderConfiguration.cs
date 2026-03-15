namespace AiDevTeam.Core.Models;

public class ProviderConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public string? DefaultModel { get; set; }
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];
    public bool IsAvailable { get; set; } = false;
    public bool IsDetected { get; set; } = false;
    public List<string> AvailableModels { get; set; } = [];
    public int DefaultTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// What this provider is best at. The orchestrator uses this to pick the right
    /// provider for each task type. Multiple capabilities can be assigned.
    /// </summary>
    public List<ProviderCapability> Capabilities { get; set; } = [];

    /// <summary>
    /// Free-text description of what this provider excels at.
    /// Shown in the UI and used by the orchestrator for routing decisions.
    /// </summary>
    public string? StrengthDescription { get; set; }

    /// <summary>
    /// Priority when multiple providers share a capability (lower = preferred).
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Maximum concurrent runs allowed for this provider. 0 = unlimited.
    /// </summary>
    public int MaxConcurrentRuns { get; set; }

    /// <summary>
    /// Cost tier indicator for the orchestrator to consider.
    /// </summary>
    public ProviderCostTier CostTier { get; set; } = ProviderCostTier.Standard;
}

/// <summary>
/// What a provider is good at. Used by the orchestrator to match tasks to providers.
/// A provider can have multiple capabilities.
/// </summary>
public enum ProviderCapability
{
    /// <summary>General-purpose coding and implementation.</summary>
    Coding,

    /// <summary>Deep reasoning, architecture decisions, complex analysis.</summary>
    Reasoning,

    /// <summary>Code review and quality analysis.</summary>
    Review,

    /// <summary>Test generation and test execution.</summary>
    Testing,

    /// <summary>GitHub API integration (issues, PRs, comments) via MCP or CLI.</summary>
    GitHubIntegration,

    /// <summary>Database design, query optimization, migrations.</summary>
    DatabaseDesign,

    /// <summary>Documentation generation.</summary>
    Documentation,

    /// <summary>Quick tasks that need fast turnaround (summaries, small edits).</summary>
    FastTasks,

    /// <summary>Security analysis and vulnerability scanning.</summary>
    Security,

    /// <summary>DevOps, CI/CD, infrastructure tasks.</summary>
    DevOps
}

public enum ProviderCostTier
{
    Free,
    Low,
    Standard,
    High,
    Premium
}
