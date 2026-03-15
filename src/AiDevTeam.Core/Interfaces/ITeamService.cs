using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

public interface ITeamService
{
    Task<List<Team>> GetAllAsync();
    Task<Team?> GetByIdAsync(string id);
    Task<Team> CreateAsync(Team team);
    Task<Team> UpdateAsync(Team team);
    Task DeleteAsync(string id);
}
