using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

public interface IProviderConfigurationService
{
    Task<List<ProviderConfiguration>> GetAllAsync();
    Task<ProviderConfiguration?> GetByNameAsync(string name);
    Task<ProviderConfiguration> SaveAsync(ProviderConfiguration config);
    Task RefreshDetectionAsync();
}
