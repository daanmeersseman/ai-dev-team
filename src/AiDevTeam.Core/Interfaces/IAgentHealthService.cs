namespace AiDevTeam.Core.Interfaces;

public interface IAgentHealthService
{
    Task<AgentHealthStatus> CheckAgentHealthAsync(string agentId);
    Task<List<AgentHealthStatus>> CheckAllAgentsHealthAsync();
    Task<bool> ValidateAgentConfigurationAsync(string agentId);
}

public class AgentHealthStatus
{
    public string AgentId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public bool IsConfigurationValid { get; set; }
    public bool IsProviderAvailable { get; set; }
    public bool IsExecutableAccessible { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan? LastResponseTime { get; set; }
}