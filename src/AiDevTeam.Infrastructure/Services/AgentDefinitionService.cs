using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;

namespace AiDevTeam.Infrastructure.Services;

public class AgentDefinitionService : IAgentDefinitionService
{
    private readonly StoragePaths _paths;

    public AgentDefinitionService(StoragePaths paths) => _paths = paths;

    public async Task<List<AgentDefinition>> GetAllAsync()
    {
        var agents = new List<AgentDefinition>();
        var dir = _paths.AgentsDir;
        if (!Directory.Exists(dir)) return agents;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var agent = await JsonStore.LoadAsync<AgentDefinition>(file);
            agents.Add(agent);
        }
        return agents.OrderBy(a => a.Role).ThenBy(a => a.Name).ToList();
    }

    public async Task<AgentDefinition?> GetByIdAsync(string id)
    {
        var file = _paths.AgentFile(id);
        if (!File.Exists(file)) return null;
        return await JsonStore.LoadAsync<AgentDefinition>(file);
    }

    public async Task<AgentDefinition> CreateAsync(AgentDefinition agent)
    {
        Directory.CreateDirectory(_paths.AgentsDir);
        await JsonStore.SaveAsync(_paths.AgentFile(agent.Id), agent);
        return agent;
    }

    public async Task<AgentDefinition> UpdateAsync(AgentDefinition agent)
    {
        await JsonStore.SaveAsync(_paths.AgentFile(agent.Id), agent);
        return agent;
    }

    public async Task DeleteAsync(string id)
    {
        var file = _paths.AgentFile(id);
        if (File.Exists(file)) File.Delete(file);
    }
}
