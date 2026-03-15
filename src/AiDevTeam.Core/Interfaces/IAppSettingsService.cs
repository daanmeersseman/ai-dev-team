using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

public interface IAppSettingsService
{
    Task<AppSettings> GetAsync();
    Task SaveAsync(AppSettings settings);
}
