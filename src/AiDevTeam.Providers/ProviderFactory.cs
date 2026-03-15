using AiDevTeam.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiDevTeam.Providers;

public static class ProviderRegistration
{
    public static IServiceCollection AddAgentProviders(this IServiceCollection services)
    {
        services.AddSingleton<IAgentProvider, MockProvider>();

        services.AddSingleton<IAgentProvider>(sp =>
            new CliAgentProvider("ClaudeCli", sp.GetRequiredService<ILogger<CliAgentProvider>>()));
        services.AddSingleton<IAgentProvider>(sp =>
            new CliAgentProvider("CodexCli", sp.GetRequiredService<ILogger<CliAgentProvider>>()));
        services.AddSingleton<IAgentProvider>(sp =>
            new CliAgentProvider("CopilotCli", sp.GetRequiredService<ILogger<CliAgentProvider>>()));

        return services;
    }
}
