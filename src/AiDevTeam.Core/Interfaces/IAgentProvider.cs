namespace AiDevTeam.Core.Interfaces;

public interface IAgentProvider
{
    string ProviderType { get; }
    Task<AgentRunResult> ExecuteAsync(AgentRunRequest request, CancellationToken cancellationToken = default);
}

public class AgentRunRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public string? ModelName { get; set; }
    public string? ExecutablePath { get; set; }
    public string? CommandTemplate { get; set; }
    public string? DefaultArguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public int TimeoutSeconds { get; set; } = 300;
    public bool VibeMode { get; set; }

    /// <summary>
    /// Whitelist of tools the agent is allowed to use.
    /// When set, only these tools are available (e.g. "Read", "Grep").
    /// Empty list means no tools allowed. Null means no restriction.
    /// </summary>
    public List<string>? AllowedTools { get; set; }

    /// <summary>
    /// Blacklist of tools the agent is NOT allowed to use.
    /// Applied after AllowedTools. (e.g. "Bash", "Edit", "Write").
    /// </summary>
    public List<string>? DisallowedTools { get; set; }

    public Action<string>? OnOutputReceived { get; set; }
}

public class AgentRunResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? Error { get; set; }
    public int ExitCode { get; set; }
    public long DurationMs { get; set; }
    public List<AgentArtifactOutput> Artifacts { get; set; } = [];
}

public class AgentArtifactOutput
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
