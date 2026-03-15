using AiDevTeam.Core.Interfaces;
using AiDevTeam.Infrastructure.FileStore;
using AiDevTeam.Infrastructure.Services;
using AiDevTeam.Infrastructure.Services.Workflow;
using AiDevTeam.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiDevTeam.Tests.DI;

public class ServiceRegistrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;

    public ServiceRegistrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AiDevTeam_Tests_" + Guid.NewGuid().ToString("N"));
        _serviceProvider = BuildServiceProvider(_tempDir);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// Replicates the service registrations from Program.cs so we can verify
    /// that the full DI graph resolves without missing dependencies.
    /// </summary>
    private static ServiceProvider BuildServiceProvider(string storagePath)
    {
        var services = new ServiceCollection();

        // Logging (required by many services)
        services.AddLogging();

        // Storage paths
        var paths = new StoragePaths(storagePath);
        services.AddSingleton(paths);

        // Core services
        services.AddSingleton<ICliDetectionService, CliDetectionService>();
        services.AddSingleton<IConversationService, ConversationService>();
        services.AddSingleton<IMessageService, MessageService>();
        services.AddSingleton<IArtifactService, ArtifactService>();
        services.AddSingleton<IAgentDefinitionService, AgentDefinitionService>();
        services.AddSingleton<IProviderConfigurationService, ProviderConfigurationService>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IAgentRoutingService, AgentRoutingService>();
        services.AddSingleton<IAgentExecutionService, AgentExecutionService>();
        services.AddSingleton<IAgentPromptService, AgentPromptService>();
        services.AddSingleton<IAgentResponseProcessor, AgentResponseProcessor>();
        services.AddSingleton<IProviderSelector, ProviderSelector>();
        services.AddSingleton<IAgentRunService, AgentRunService>();
        services.AddSingleton<IAgentHealthService, AgentHealthService>();
        services.AddSingleton<IGitHubIssueService, GitHubIssueService>();
        services.AddSingleton<IContextBlockService, ContextBlockService>();
        services.AddSingleton<ITeamService, TeamService>();
        services.AddSingleton<IMarkdownService, MarkdownService>();

        // Workflow engine
        services.AddSingleton<IWorkflowStateMachine, WorkflowStateMachine>();
        services.AddSingleton<IWorkflowExecutionService, WorkflowExecutionService>();
        services.AddSingleton<IPromptBuilder, PromptBuilder>();
        services.AddSingleton<IOutputParser, OutputParser>();
        services.AddSingleton<IChatMessageComposer, ChatMessageComposer>();
        services.AddSingleton<IWorkflowEngine, WorkflowEngine>();

        // Seed data service
        var seedDataPath = Path.Combine(AppContext.BaseDirectory, "SeedData");
        services.AddSingleton<ISeedDataService>(sp =>
            new SeedDataService(
                sp.GetRequiredService<IAgentDefinitionService>(),
                sp.GetRequiredService<ITeamService>(),
                sp.GetRequiredService<IAppSettingsService>(),
                sp.GetRequiredService<IProviderConfigurationService>(),
                sp.GetRequiredService<ICliDetectionService>(),
                sp.GetRequiredService<IConversationService>(),
                sp.GetRequiredService<IMessageService>(),
                sp.GetRequiredService<IArtifactService>(),
                sp.GetRequiredService<IContextBlockService>(),
                sp.GetRequiredService<StoragePaths>(),
                seedDataPath,
                sp.GetRequiredService<ILogger<SeedDataService>>()));

        // Providers
        services.AddAgentProviders();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    // ── Core services ────────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(IConversationService))]
    [InlineData(typeof(IMessageService))]
    [InlineData(typeof(IArtifactService))]
    [InlineData(typeof(IAgentDefinitionService))]
    [InlineData(typeof(IProviderConfigurationService))]
    [InlineData(typeof(IAppSettingsService))]
    [InlineData(typeof(ICliDetectionService))]
    [InlineData(typeof(IProviderSelector))]
    [InlineData(typeof(IAgentRunService))]
    [InlineData(typeof(IAgentHealthService))]
    [InlineData(typeof(IGitHubIssueService))]
    [InlineData(typeof(IContextBlockService))]
    [InlineData(typeof(ITeamService))]
    [InlineData(typeof(IMarkdownService))]
    [InlineData(typeof(ISeedDataService))]
    public void CoreService_CanBeResolved(Type serviceType)
    {
        var service = _serviceProvider.GetService(serviceType);

        Assert.NotNull(service);
    }

    // ── Workflow services ────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(IWorkflowEngine))]
    [InlineData(typeof(IWorkflowStateMachine))]
    [InlineData(typeof(IWorkflowExecutionService))]
    [InlineData(typeof(IPromptBuilder))]
    [InlineData(typeof(IOutputParser))]
    [InlineData(typeof(IChatMessageComposer))]
    public void WorkflowService_CanBeResolved(Type serviceType)
    {
        var service = _serviceProvider.GetService(serviceType);

        Assert.NotNull(service);
    }

    // ── Decomposed agent services ────────────────────────────────────

    [Theory]
    [InlineData(typeof(IAgentRoutingService))]
    [InlineData(typeof(IAgentExecutionService))]
    [InlineData(typeof(IAgentPromptService))]
    [InlineData(typeof(IAgentResponseProcessor))]
    public void DecomposedAgentService_CanBeResolved(Type serviceType)
    {
        var service = _serviceProvider.GetService(serviceType);

        Assert.NotNull(service);
    }

    // ── Providers ────────────────────────────────────────────────────

    [Fact]
    public void AgentProviders_AllRegistered()
    {
        var providers = _serviceProvider.GetServices<IAgentProvider>().ToList();

        Assert.True(providers.Count >= 4, $"Expected at least 4 providers, got {providers.Count}");

        var types = providers.Select(p => p.ProviderType).ToList();
        Assert.Contains("Mock", types);
        Assert.Contains("ClaudeCli", types);
        Assert.Contains("CodexCli", types);
        Assert.Contains("CopilotCli", types);
    }

    // ── StoragePaths ─────────────────────────────────────────────────

    [Fact]
    public void StoragePaths_IsRegisteredAndPointsToConfiguredDir()
    {
        var paths = _serviceProvider.GetService<StoragePaths>();

        Assert.NotNull(paths);
        Assert.Equal(_tempDir, paths!.BasePath);
    }
}
