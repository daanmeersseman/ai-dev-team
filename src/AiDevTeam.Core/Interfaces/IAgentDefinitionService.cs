using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

public interface IAgentDefinitionService
{
    Task<List<AgentDefinition>> GetAllAsync();
    Task<AgentDefinition?> GetByIdAsync(string id);
    Task<AgentDefinition> CreateAsync(AgentDefinition agent);
    Task<AgentDefinition> UpdateAsync(AgentDefinition agent);
    Task DeleteAsync(string id);
}
