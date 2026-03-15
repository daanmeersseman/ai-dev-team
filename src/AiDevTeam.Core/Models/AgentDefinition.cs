namespace AiDevTeam.Core.Models;

public class AgentDefinition
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public AgentRole Role { get; set; }
    public string Description { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;

    // ── Character & Personality ──────────────────────────────────────
    public string Personality { get; set; } = string.Empty;
    public string CommunicationStyle { get; set; } = "professional";
    public string? CustomInstructions { get; set; }

    /// <summary>
    /// Detailed expertise areas this agent specializes in.
    /// Used by the orchestrator to assign the right tasks.
    /// </summary>
    public List<string> Expertise { get; set; } = [];

    /// <summary>
    /// Preferred provider capability. When set, the workflow engine will try to use
    /// a provider that matches this capability for this agent's runs.
    /// </summary>
    public ProviderCapability? PreferredProviderCapability { get; set; }

    /// <summary>
    /// Background story / persona details. Makes the agent feel like a real team member.
    /// This is included in the system prompt to give the agent a consistent character.
    /// </summary>
    public string? Backstory { get; set; }

    /// <summary>
    /// What this agent values most in their work. Influences how they approach tasks.
    /// </summary>
    public List<string> Values { get; set; } = [];

    /// <summary>
    /// Known quirks or patterns in how this agent communicates.
    /// E.g., "Uses bullet points", "Asks clarifying questions before starting", "Dry humor".
    /// </summary>
    public List<string> CommunicationQuirks { get; set; } = [];

    // ── Provider ─────────────────────────────────────────────────────
    public string ProviderType { get; set; } = "Mock";
    public string? ModelName { get; set; }
    public string? CommandTemplate { get; set; }
    public string? ExecutablePath { get; set; }
    public string? DefaultArguments { get; set; }

    /// <summary>
    /// Fallback provider if the primary is unavailable.
    /// </summary>
    public string? FallbackProviderType { get; set; }
    public string? FallbackModelName { get; set; }

    // ── Status ───────────────────────────────────────────────────────
    public bool IsEnabled { get; set; } = true;

    // ── Appearance ───────────────────────────────────────────────────
    public string Color { get; set; } = "#1976D2";
    public string? AvatarInitials { get; set; }
    public string? AvatarUrl { get; set; }

    // ── Performance / Token Optimization ─────────────────────────────
    public int MaxContextMessages { get; set; } = 5;
    public int MaxContextTokens { get; set; } = 2000;

    // ── Permissions ──────────────────────────────────────────────────
    public bool CanCreateArtifacts { get; set; } = true;
    public bool CanTriggerAgents { get; set; } = false;
    public bool CanExecuteCommands { get; set; } = false;
    public bool VibeMode { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 300;
    public string? WorkingDirectoryStrategy { get; set; }

    /// <summary>
    /// Whitelist of CLI tools this agent may use (e.g. "Read", "Grep").
    /// When set, only these tools are available. Empty list = no tools.
    /// Null = no restriction (all tools available).
    /// </summary>
    public List<string>? AllowedTools { get; set; }

    /// <summary>
    /// Blacklist of CLI tools this agent may NOT use (e.g. "Bash", "Edit", "Write").
    /// </summary>
    public List<string>? DisallowedTools { get; set; }
}

public enum AgentRole
{
    Orchestrator,
    Reviewer,
    Coder,
    Tester,
    DatabaseSpecialist,
    Custom
}
