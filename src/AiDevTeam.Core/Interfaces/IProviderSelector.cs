using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

public interface IProviderSelector
{
    Task<IAgentProvider?> SelectProviderAsync(ProviderCapability? requiredCapability, string? preferredProviderType = null);
    Task<IAgentProvider> SelectProviderForAgentAsync(AgentDefinition agent);
}
