using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;

namespace AiDevTeam.Infrastructure.Services;

public class TeamService : ITeamService
{
    private readonly StoragePaths _paths;

    public TeamService(StoragePaths paths) => _paths = paths;

    public async Task<List<Team>> GetAllAsync()
    {
        var teams = new List<Team>();
        var dir = _paths.TeamsDir;
        if (!Directory.Exists(dir)) return teams;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var team = await JsonStore.LoadAsync<Team>(file);
            teams.Add(team);
        }
        return teams.OrderBy(t => t.Name).ToList();
    }

    public async Task<Team?> GetByIdAsync(string id)
    {
        var file = _paths.TeamFile(id);
        if (!File.Exists(file)) return null;
        return await JsonStore.LoadAsync<Team>(file);
    }

    public async Task<Team> CreateAsync(Team team)
    {
        Directory.CreateDirectory(_paths.TeamsDir);
        await JsonStore.SaveAsync(_paths.TeamFile(team.Id), team);
        return team;
    }

    public async Task<Team> UpdateAsync(Team team)
    {
        await JsonStore.SaveAsync(_paths.TeamFile(team.Id), team);
        return team;
    }

    public async Task DeleteAsync(string id)
    {
        var file = _paths.TeamFile(id);
        if (File.Exists(file)) File.Delete(file);
    }
}
