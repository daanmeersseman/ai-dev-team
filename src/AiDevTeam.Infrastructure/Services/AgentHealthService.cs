using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Core.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AiDevTeam.Infrastructure.Services;

public class AgentHealthService : IAgentHealthService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentHealthService> _logger;

    public AgentHealthService(IServiceProvider serviceProvider, ILogger<AgentHealthService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<AgentHealthStatus> CheckAgentHealthAsync(string agentId)
    {
        var status = new AgentHealthStatus { AgentId = agentId };
        
        try
        {
            var agentService = _serviceProvider.GetRequiredService<IAgentDefinitionService>();
            var agent = await agentService.GetByIdAsync(agentId);
            
            if (agent == null)
            {
                status.ErrorMessage = "Agent not found";
                return status;
            }

            status.AgentName = agent.Name;
            status.IsConfigurationValid = await ValidateAgentConfigurationAsync(agentId);

            // Check provider availability
            var providers = _serviceProvider.GetServices<IAgentProvider>();
            var provider = providers.FirstOrDefault(p => p.ProviderType == agent.ProviderType);
            status.IsProviderAvailable = provider != null;

            if (!status.IsProviderAvailable)
            {
                status.ErrorMessage = $"Provider '{agent.ProviderType}' not available";
                return status;
            }

            // Check executable accessibility (if configured)
            status.IsExecutableAccessible = await CheckExecutableAsync(agent);

            // Quick health check with a simple prompt
            if (status.IsConfigurationValid && status.IsProviderAvailable && status.IsExecutableAccessible)
            {
                var (isHealthy, responseTime, error) = await PerformHealthCheckAsync(agent, provider!);
                status.IsHealthy = isHealthy;
                status.LastResponseTime = responseTime;
                if (!isHealthy)
                    status.ErrorMessage = error;
            }

            _logger.LogDebug("Health check for {Agent}: Healthy={Healthy}, Config={Config}, Provider={Provider}, Executable={Executable}",
                agent.Name, status.IsHealthy, status.IsConfigurationValid, status.IsProviderAvailable, status.IsExecutableAccessible);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for agent {AgentId}: {Error}", agentId, ex.Message);
            status.ErrorMessage = ex.Message;
        }

        return status;
    }

    public async Task<List<AgentHealthStatus>> CheckAllAgentsHealthAsync()
    {
        var agentService = _serviceProvider.GetRequiredService<IAgentDefinitionService>();
        var agents = await agentService.GetAllAsync();
        
        var healthStatuses = new List<AgentHealthStatus>();
        
        foreach (var agent in agents.Where(a => a.IsEnabled))
        {
            var status = await CheckAgentHealthAsync(agent.Id);
            healthStatuses.Add(status);
        }

        return healthStatuses;
    }

    public async Task<bool> ValidateAgentConfigurationAsync(string agentId)
    {
        try
        {
            var agentService = _serviceProvider.GetRequiredService<IAgentDefinitionService>();
            var agent = await agentService.GetByIdAsync(agentId);
            
            if (agent == null) return false;
            if (!agent.IsEnabled) return false;
            if (string.IsNullOrWhiteSpace(agent.Name)) return false;
            if (string.IsNullOrWhiteSpace(agent.ProviderType)) return false;
            if (agent.TimeoutSeconds <= 0 || agent.TimeoutSeconds > 1800) return false;

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Configuration validation failed for agent {AgentId}", agentId);
            return false;
        }
    }

    private async Task<bool> CheckExecutableAsync(AgentDefinition agent)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(agent.ExecutablePath))
                return true; // No specific executable configured, assume default is OK

            if (Path.IsPathRooted(agent.ExecutablePath))
                return File.Exists(agent.ExecutablePath);

            // Try to resolve from PATH
            return ExecutableResolver.Resolve(agent.ExecutablePath) != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Executable check failed for agent {Agent}", agent.Name);
            return false;
        }
    }

    private async Task<(bool IsHealthy, TimeSpan? ResponseTime, string? Error)> PerformHealthCheckAsync(
        AgentDefinition agent, IAgentProvider provider)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            
            var request = new AgentRunRequest
            {
                Prompt = "Say 'OK' (health check)",
                SystemPrompt = "You are a health check. Reply with only 'OK'.",
                ModelName = agent.ModelName,
                ExecutablePath = agent.ExecutablePath,
                CommandTemplate = agent.CommandTemplate,
                DefaultArguments = agent.DefaultArguments,
                TimeoutSeconds = Math.Min(agent.TimeoutSeconds, 30), // Cap health check timeout
                VibeMode = agent.VibeMode
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await provider.ExecuteAsync(request, cts.Token);
            
            sw.Stop();

            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
            {
                return (true, sw.Elapsed, null);
            }

            return (false, sw.Elapsed, result.Error ?? "No output received");
        }
        catch (OperationCanceledException)
        {
            return (false, TimeSpan.FromSeconds(30), "Health check timed out");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

}