using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

public interface ICliDetectionService
{
    Task<ProviderConfiguration> DetectProviderAsync(string providerName);
    Task<List<ProviderConfiguration>> DetectAllProvidersAsync();
    Task<List<string>> GetAvailableModelsAsync(string providerName, string? executablePath = null);
}
