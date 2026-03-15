using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;

namespace AiDevTeam.Infrastructure.Services;

public class AppSettingsService : IAppSettingsService
{
    private readonly StoragePaths _paths;

    public AppSettingsService(StoragePaths paths) => _paths = paths;

    public async Task<AppSettings> GetAsync()
    {
        var settings = await JsonStore.LoadAsync<AppSettings>(_paths.SettingsFile);
        if (string.IsNullOrEmpty(settings.WorkspacePath))
            settings.WorkspacePath = _paths.BasePath;
        return settings;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await JsonStore.SaveAsync(_paths.SettingsFile, settings);
    }
}
