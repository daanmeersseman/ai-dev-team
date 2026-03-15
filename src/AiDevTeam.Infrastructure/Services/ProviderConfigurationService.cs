using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;

namespace AiDevTeam.Infrastructure.Services;

public class ProviderConfigurationService : IProviderConfigurationService
{
    private readonly StoragePaths _paths;
    private readonly ICliDetectionService _detection;

    public ProviderConfigurationService(StoragePaths paths, ICliDetectionService detection)
    {
        _paths = paths;
        _detection = detection;
    }

    public async Task<List<ProviderConfiguration>> GetAllAsync()
    {
        var providers = new List<ProviderConfiguration>();
        var dir = _paths.ProvidersDir;
        if (!Directory.Exists(dir)) return providers;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var provider = await JsonStore.LoadAsync<ProviderConfiguration>(file);
            providers.Add(provider);
        }
        return providers.OrderBy(p => p.Name).ToList();
    }

    public async Task<ProviderConfiguration?> GetByNameAsync(string name)
    {
        var file = _paths.ProviderFile(name);
        if (!File.Exists(file)) return null;
        return await JsonStore.LoadAsync<ProviderConfiguration>(file);
    }

    public async Task<ProviderConfiguration> SaveAsync(ProviderConfiguration config)
    {
        Directory.CreateDirectory(_paths.ProvidersDir);
        await JsonStore.SaveAsync(_paths.ProviderFile(config.Name), config);
        return config;
    }

    public async Task RefreshDetectionAsync()
    {
        var detected = await _detection.DetectAllProvidersAsync();
        foreach (var provider in detected)
        {
            var existing = await GetByNameAsync(provider.Name);
            if (existing != null)
            {
                existing.IsDetected = provider.IsDetected;
                existing.AvailableModels = provider.AvailableModels;
                if (string.IsNullOrEmpty(existing.ExecutablePath))
                    existing.ExecutablePath = provider.ExecutablePath;
                await SaveAsync(existing);
            }
            else
            {
                await SaveAsync(provider);
            }
        }
    }
}
