using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using Microsoft.Extensions.Logging;

namespace AiDevTeam.Infrastructure.Services;

public class ProviderSelector : IProviderSelector
{
    private readonly IEnumerable<IAgentProvider> _providers;
    private readonly IProviderConfigurationService _configService;
    private readonly ILogger<ProviderSelector> _logger;

    public ProviderSelector(
        IEnumerable<IAgentProvider> providers,
        IProviderConfigurationService configService,
        ILogger<ProviderSelector> logger)
    {
        _providers = providers;
        _configService = configService;
        _logger = logger;
    }

    public async Task<IAgentProvider?> SelectProviderAsync(ProviderCapability? requiredCapability, string? preferredProviderType = null)
    {
        // 1. If preferredProviderType set, find matching provider
        if (!string.IsNullOrEmpty(preferredProviderType))
        {
            var preferred = _providers.FirstOrDefault(p => p.ProviderType == preferredProviderType);
            if (preferred != null) return preferred;
        }

        // 2. If no capability required, return first available
        if (requiredCapability == null)
            return _providers.FirstOrDefault();

        // 3. Load configs and find providers with matching capability, sorted by priority
        var configs = await _configService.GetAllAsync();
        var matching = configs
            .Where(c => c.IsAvailable && c.Capabilities.Contains(requiredCapability.Value))
            .OrderBy(c => c.Priority)
            .ToList();

        foreach (var config in matching)
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderType == config.Name);
            if (provider != null) return provider;
        }

        // 4. Fallback to any available provider
        _logger.LogWarning("No provider found with capability {Capability}, falling back to first available", requiredCapability);
        return _providers.FirstOrDefault();
    }

    public async Task<IAgentProvider> SelectProviderForAgentAsync(AgentDefinition agent)
    {
        // Try agent's configured provider first
        var result = await SelectProviderAsync(null, agent.ProviderType);
        if (result != null) return result;

        // Then try by preferred capability
        if (agent.PreferredProviderCapability != null)
        {
            result = await SelectProviderAsync(agent.PreferredProviderCapability);
            if (result != null) return result;
        }

        // Then try fallback provider
        if (!string.IsNullOrEmpty(agent.FallbackProviderType))
        {
            result = await SelectProviderAsync(null, agent.FallbackProviderType);
            if (result != null) return result;
        }

        // Any available
        return _providers.First();
    }
}
