using AiDevTeam.Core.Interfaces;
using AiDevTeam.Infrastructure.FileStore;
using AiDevTeam.Infrastructure.Services;
using AiDevTeam.Infrastructure.Services.Workflow;
using AiDevTeam.Providers;
using AiDevTeam.Web.Components;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// MudBlazor
builder.Services.AddMudServices();

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// File-based storage — stored in user profile so it survives rebuilds
var storagePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiDevTeam");
var paths = new StoragePaths(storagePath);
builder.Services.AddSingleton(paths);

// Services
builder.Services.AddSingleton<ICliDetectionService, CliDetectionService>();
builder.Services.AddSingleton<IConversationService, ConversationService>();
builder.Services.AddSingleton<IMessageService, MessageService>();
builder.Services.AddSingleton<IArtifactService, ArtifactService>();
builder.Services.AddSingleton<IAgentDefinitionService, AgentDefinitionService>();
builder.Services.AddSingleton<IProviderConfigurationService, ProviderConfigurationService>();
builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();
builder.Services.AddSingleton<IAgentRoutingService, AgentRoutingService>();
builder.Services.AddSingleton<IAgentExecutionService, AgentExecutionService>();
builder.Services.AddSingleton<IAgentPromptService, AgentPromptService>();
builder.Services.AddSingleton<IAgentResponseProcessor, AgentResponseProcessor>();
builder.Services.AddSingleton<IProviderSelector, ProviderSelector>();
builder.Services.AddSingleton<IAgentRunService, AgentRunService>();
builder.Services.AddSingleton<IAgentHealthService, AgentHealthService>();
builder.Services.AddSingleton<IGitHubIssueService, GitHubIssueService>();
builder.Services.AddSingleton<IContextBlockService, ContextBlockService>();
builder.Services.AddSingleton<ITeamService, TeamService>();
builder.Services.AddSingleton<IMarkdownService, MarkdownService>();

// Workflow engine
builder.Services.AddSingleton<IWorkflowStateMachine, WorkflowStateMachine>();
builder.Services.AddSingleton<IWorkflowExecutionService, WorkflowExecutionService>();
builder.Services.AddSingleton<IPromptBuilder, PromptBuilder>();
builder.Services.AddSingleton<IOutputParser, OutputParser>();
builder.Services.AddSingleton<IChatMessageComposer, ChatMessageComposer>();
builder.Services.AddSingleton<IWorkflowEngine, WorkflowEngine>();

// Seed data service
var seedDataPath = Path.Combine(AppContext.BaseDirectory, "SeedData");
builder.Services.AddSingleton<ISeedDataService>(sp =>
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
builder.Services.AddAgentProviders();

var app = builder.Build();

// Seed data if empty
var seedService = app.Services.GetRequiredService<ISeedDataService>();
await seedService.SeedAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
