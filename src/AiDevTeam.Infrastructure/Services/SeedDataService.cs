using System.Text.Json;
using System.Text.Json.Serialization;
using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;
using Microsoft.Extensions.Logging;

namespace AiDevTeam.Infrastructure.Services;

public class SeedDataService : ISeedDataService
{
    private readonly IAgentDefinitionService _agentService;
    private readonly ITeamService _teamService;
    private readonly IAppSettingsService _settingsService;
    private readonly IProviderConfigurationService _providerService;
    private readonly ICliDetectionService _detectionService;
    private readonly IConversationService _convService;
    private readonly IMessageService _msgService;
    private readonly IArtifactService _artifactService;
    private readonly IContextBlockService _contextService;
    private readonly StoragePaths _paths;
    private readonly string _seedDataPath;
    private readonly ILogger<SeedDataService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public SeedDataService(
        IAgentDefinitionService agentService,
        ITeamService teamService,
        IAppSettingsService settingsService,
        IProviderConfigurationService providerService,
        ICliDetectionService detectionService,
        IConversationService convService,
        IMessageService msgService,
        IArtifactService artifactService,
        IContextBlockService contextService,
        StoragePaths paths,
        string seedDataPath,
        ILogger<SeedDataService> logger)
    {
        _agentService = agentService;
        _teamService = teamService;
        _settingsService = settingsService;
        _providerService = providerService;
        _detectionService = detectionService;
        _convService = convService;
        _msgService = msgService;
        _artifactService = artifactService;
        _contextService = contextService;
        _paths = paths;
        _seedDataPath = seedDataPath;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        await MigrateOrchestratorAsync();

        var existing = await _agentService.GetAllAsync();
        if (existing.Any()) return; // Already seeded

        _logger.LogInformation("Seeding initial data...");

        await DetectProvidersAsync();
        await SeedProvidersAsync();
        var agents = await SeedAgentsAsync();
        await SeedTeamAsync(agents);
        await SeedTeamFlowAsync();
        await SeedSampleConversationAsync();

        _logger.LogInformation("Seed data complete.");
    }

    private async Task MigrateOrchestratorAsync()
    {
        var existing = await _agentService.GetAllAsync();
        var orchestrator = existing.FirstOrDefault(a => a.Role == AgentRole.Orchestrator);
        if (orchestrator == null) return;

        var changed = false;

        if (orchestrator.VibeMode)
        {
            orchestrator.VibeMode = false;
            orchestrator.CanCreateArtifacts = false;
            orchestrator.CanExecuteCommands = false;
            orchestrator.TimeoutSeconds = 60;
            orchestrator.SystemPrompt = "You ARE Alex, a Tech Lead. This is your identity \u2014 never say you are an AI, Claude, or a language model. You are a real team member named Alex. You analyze tasks, break them down, create plans, and coordinate work across the team. You NEVER write code or create files yourself \u2014 you delegate to your team.";
            changed = true;
        }

        // Disable all tools for the orchestrator — it should only return JSON text, never use filesystem/shell tools
        if (orchestrator.AllowedTools == null)
        {
            orchestrator.AllowedTools = []; // empty list = no tools allowed
            changed = true;
        }

        if (changed)
            await _agentService.UpdateAsync(orchestrator);
    }

    private async Task DetectProvidersAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _providerService.RefreshDetectionAsync().WaitAsync(cts.Token);
        }
        catch
        {
            // OK if detection fails or times out
        }
    }

    private async Task SeedProvidersAsync()
    {
        var json = await ReadSeedFileAsync("providers.json");
        var providerDefs = JsonSerializer.Deserialize<List<ProviderSeedData>>(json, JsonOpts);
        if (providerDefs == null) return;

        foreach (var def in providerDefs)
        {
            if (def.Name == "Mock")
            {
                // Always ensure Mock provider exists
                var mockProvider = await _providerService.GetByNameAsync("Mock");
                if (mockProvider == null)
                {
                    await _providerService.SaveAsync(new ProviderConfiguration
                    {
                        Name = def.Name,
                        IsAvailable = def.IsAvailable,
                        IsDetected = def.IsDetected,
                        AvailableModels = def.AvailableModels ?? new(),
                        Capabilities = def.Capabilities ?? new(),
                        StrengthDescription = def.StrengthDescription,
                        CostTier = def.CostTier,
                        Priority = def.Priority
                    });
                }
            }
            else
            {
                // For detected CLI providers, only update capabilities if they exist but have none
                var existing = await _providerService.GetByNameAsync(def.Name);
                if (existing != null && existing.Capabilities.Count == 0)
                {
                    existing.Capabilities = def.Capabilities ?? new();
                    existing.StrengthDescription = def.StrengthDescription;
                    existing.CostTier = def.CostTier;
                    existing.Priority = def.Priority;
                    await _providerService.SaveAsync(existing);
                }
            }
        }
    }

    private async Task<List<AgentDefinition>> SeedAgentsAsync()
    {
        var json = await ReadSeedFileAsync("agents.json");
        var agentDefs = JsonSerializer.Deserialize<List<AgentSeedData>>(json, JsonOpts);
        if (agentDefs == null) return new();

        var created = new List<AgentDefinition>();
        foreach (var def in agentDefs)
        {
            var agent = new AgentDefinition
            {
                Name = def.Name,
                Role = def.Role,
                Description = def.Description,
                SystemPrompt = def.SystemPrompt,
                Personality = def.Personality,
                Backstory = def.Backstory,
                Expertise = def.Expertise ?? new(),
                Values = def.Values ?? new(),
                CommunicationQuirks = def.CommunicationQuirks ?? new(),
                CommunicationStyle = def.CommunicationStyle ?? "professional",
                PreferredProviderCapability = def.PreferredProviderCapability,
                ProviderType = def.ProviderType ?? "Mock",
                ModelName = def.ModelName,
                Color = def.Color ?? "#1976D2",
                AvatarInitials = def.AvatarInitials,
                CanCreateArtifacts = def.CanCreateArtifacts,
                CanTriggerAgents = def.CanTriggerAgents,
                CanExecuteCommands = def.CanExecuteCommands,
                VibeMode = def.VibeMode,
                TimeoutSeconds = def.TimeoutSeconds > 0 ? def.TimeoutSeconds : 300
            };
            await _agentService.CreateAsync(agent);
            created.Add(agent);
        }

        return created;
    }

    private async Task SeedTeamAsync(List<AgentDefinition> agents)
    {
        var json = await ReadSeedFileAsync("team.json");
        var teamDef = JsonSerializer.Deserialize<TeamSeedData>(json, JsonOpts);
        if (teamDef == null) return;

        // Resolve agent names to IDs
        var agentLookup = agents.ToDictionary(a => a.Name, a => a.Id);
        var agentIds = new List<string>();
        foreach (var name in teamDef.AgentNames ?? new())
        {
            if (agentLookup.TryGetValue(name, out var id))
                agentIds.Add(id);
            else
                _logger.LogWarning("Seed team references unknown agent '{Name}'", name);
        }

        await _teamService.CreateAsync(new Team
        {
            Name = teamDef.Name ?? "Full Stack Team",
            Description = teamDef.Description ?? string.Empty,
            AgentIds = agentIds
        });
    }

    private async Task SeedTeamFlowAsync()
    {
        var json = await ReadSeedFileAsync("teamflow.json");
        var flowDef = JsonSerializer.Deserialize<TeamFlowSeedData>(json, JsonOpts);
        if (flowDef == null) return;

        var steps = (flowDef.Steps ?? new()).Select(s => new FlowStep
        {
            Order = s.Order,
            AgentRole = s.AgentRole ?? string.Empty,
            Action = s.Action ?? string.Empty,
            ReportsTo = s.ReportsTo,
            Condition = s.Condition,
            IsOptional = s.IsOptional,
            ChatTemplate = s.ChatTemplate,
            NextStepOnSuccess = s.NextStepOnSuccess,
            NextStepOnChangesRequested = s.NextStepOnChangesRequested,
            RequiredCapability = s.RequiredCapability
        }).ToList();

        await _settingsService.SaveAsync(new AppSettings
        {
            WorkspacePath = _paths.BasePath,
            DefaultCommunicationStyle = flowDef.DefaultCommunicationStyle ?? "professional",
            TeamFlow = new TeamFlowConfiguration
            {
                Name = flowDef.Name ?? "Default Flow",
                Description = flowDef.Description ?? string.Empty,
                MaxReviewCycles = flowDef.MaxReviewCycles,
                MaxRetriesPerStep = flowDef.MaxRetriesPerStep,
                RequireUserApprovalBeforeCoding = flowDef.RequireUserApprovalBeforeCoding,
                RunTestsAfterReview = flowDef.RunTestsAfterReview,
                Steps = steps
            }
        });
    }

    private async Task SeedSampleConversationAsync()
    {
        var conv = await _convService.CreateAsync(
            "Add user authentication",
            "Implement JWT-based authentication with login/register endpoints and role-based access control.",
            Priority.High,
            "backend,security,feature");
        await _convService.UpdateStatusAsync(conv.Id, ConversationStatus.InProgress);

#pragma warning disable CS0618 // Using obsolete AddAsync overload for seed data compatibility
        await _msgService.AddAsync(conv.Id, "You", "User", MessageType.UserInstruction,
            "We need to add JWT-based authentication to our API. Include login, register, and role-based access control.");

        // Alex's plan as a context block
        var alexBlock = await _contextService.CreateAsync(conv.Id, "", "task analysis",
            "## Authentication Plan\n\n### Phase 1: Setup\n1. Add Identity + JWT packages\n2. Configure Identity with EF Core\n3. Create `ApplicationUser` entity\n\n### Phase 2: Implementation\n4. `AuthController` with Login/Register\n5. JWT token generation service\n6. JWT middleware\n\n### Phase 3: Authorization\n7. Role-based policies\n8. Protect endpoints\n\n### Team\n- **Sam**: Implementation\n- **Jordan**: Schema review\n- **Riley**: Integration tests\n- **Morgan**: Code review",
            "Alex (Tech Lead)");

        await _msgService.AddAsync(conv.Id, "Alex (Tech Lead)", "Orchestrator", MessageType.Plan,
            $"I've analyzed this and put together a plan. We'll need Identity + JWT packages, an AuthController with login/register, and role-based policies. Check out [[block:{alexBlock.Id}:my analysis]] for the full breakdown.\n\nSam, can you handle the implementation? Jordan, please review the DB impact. Morgan, you're on code review once Sam is done. Riley, prep the integration tests.");

        // Jordan's DB analysis as a context block
        var jordanBlock = await _contextService.CreateAsync(conv.Id, "", "DB analysis",
            "## DB Impact\n\nIdentity adds these tables: `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`.\n\nRecommendation: separate `AuthDbContext`. Add index on `Email`.\n\n### Migration Notes\n- Tables use `nvarchar(450)` keys by default\n- Consider custom `ApplicationUser` with additional profile fields\n- Clustered index on `NormalizedEmail` for login performance",
            "Jordan (DB Specialist)");

        await _msgService.AddAsync(conv.Id, "Jordan (DB Specialist)", "DatabaseSpecialist", MessageType.AgentThoughtSummary,
            $"Analyzed the DB impact. Identity will add 5 tables \u2014 nothing too heavy. I'd recommend a separate `AuthDbContext` and an index on Email for login perf. See [[block:{jordanBlock.Id}:DB analysis]] for the full details.");

        // Sam's implementation as a context block
        var samBlock = await _contextService.CreateAsync(conv.Id, "", "implementation details",
            "## Implementation Summary\n\n### Files Created\n- `AuthController.cs` \u2014 Login/Register endpoints\n- `JwtTokenService.cs` \u2014 Token generation with configurable expiry\n- `JwtMiddleware.cs` \u2014 Request pipeline authentication\n- `ApplicationUser.cs` \u2014 Extended IdentityUser\n\n### Key Decisions\n- 1h token expiry with refresh token support\n- Minimum 8 character passwords with complexity rules\n- BCrypt for password hashing via Identity defaults\n- Claims-based role authorization\n\n### Dependencies Added\n- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`\n- `Microsoft.AspNetCore.Authentication.JwtBearer`\n- `System.IdentityModel.Tokens.Jwt`",
            "Sam (Coder)");

        await _msgService.AddAsync(conv.Id, "Sam (Coder)", "Coder", MessageType.ArtifactCreated,
            $"Done! JWT service, AuthController, and middleware are all in place. Used 1h token expiry with refresh support and 8-char minimum passwords. See [[block:{samBlock.Id}:implementation details]] for the full rundown. Ready for review, Morgan!");

        // Morgan's review as a context block
        var morganBlock = await _contextService.CreateAsync(conv.Id, "", "code review",
            "## Review: Auth Implementation\n\n### Strengths\n- Clean separation of concerns between controller, service, and middleware\n- Proper async/await throughout\n- Good error handling with appropriate HTTP status codes\n- Claims-based approach is extensible\n\n### Suggestions\n1. **Extract JWT config** to appsettings.json instead of hardcoded values\n2. **Add rate limiting** on login endpoint to prevent brute force\n3. **Implement refresh token rotation** for better security\n4. **Add request validation** with FluentValidation\n\n### Security Notes\n- Token storage on client side should use httpOnly cookies\n- Consider adding CORS configuration\n- Add audit logging for auth events\n\n### Verdict: Approved with suggestions",
            "Morgan (Reviewer)");

        await _msgService.AddAsync(conv.Id, "Morgan (Reviewer)", "Reviewer", MessageType.Review,
            $"I've reviewed Sam's implementation. Overall it looks solid \u2014 clean separation, proper async, good error handling. I have a few suggestions around config extraction and rate limiting. See [[block:{morganBlock.Id}:my review]] for details. Approved with suggestions!");

        // Riley's test results as a context block
        var rileyBlock = await _contextService.CreateAsync(conv.Id, "", "test results",
            "## Test Results\n\n| Test | Result |\n|------|--------|\n| Register valid user | \u2705 Pass |\n| Duplicate email rejection | \u2705 Pass |\n| Valid login returns token | \u2705 Pass |\n| Invalid password returns 401 | \u2705 Pass |\n| No token \u2192 401 | \u2705 Pass |\n| Valid token \u2192 200 | \u2705 Pass |\n| Wrong role \u2192 403 | \u2705 Pass |\n| Expired token \u2192 401 | \u274c Fail |\n\n### Failure Details\n**Expired token test**: System clock can't be mocked in integration test. Need `ISystemClock` abstraction or `TimeProvider` (.NET 8+) to control time in tests.\n\n### Coverage\n- Controller: 92%\n- Service: 88%\n- Middleware: 95%",
            "Riley (Tester)");

        await _msgService.AddAsync(conv.Id, "Riley (Tester)", "Tester", MessageType.TestResult,
            $"Test suite is done! 7 out of 8 passed. The expired token test needs a clock mock \u2014 I'll need a `TimeProvider` abstraction to fix that one. See [[block:{rileyBlock.Id}:test results]] for the full breakdown.");
#pragma warning restore CS0618

        // Seed artifacts
        await _artifactService.CreateAsync(conv.Id, "task.md", ArtifactType.Markdown,
            "# Add User Authentication\n\n## Requirements\n- JWT-based auth\n- Login/Register endpoints\n- Role-based access control\n\n## Status: In Progress\n## Priority: High", "System");

        await _artifactService.CreateAsync(conv.Id, "plan.md", ArtifactType.Markdown,
            "# Implementation Plan\n\n## Phase 1: Setup\n- [x] Add Identity packages\n- [x] Configure Identity\n- [x] ApplicationUser entity\n\n## Phase 2: Implementation\n- [x] AuthController\n- [x] JWT Token Service\n- [ ] Refresh token rotation\n\n## Phase 3: Testing\n- [x] Integration tests (7/8)\n- [ ] Fix clock mocking", "Alex (Tech Lead)");

        await _artifactService.CreateAsync(conv.Id, "review.md", ArtifactType.Markdown,
            "# Code Review\n\n**Reviewer:** Morgan\n\n## Verdict: Approved\n\n### Strengths\n- Clean JWT service\n- Proper async\n- Good validation\n\n### Action Items\n1. Move JWT config to appsettings\n2. Add rate limiting\n3. Refresh token rotation", "Morgan (Reviewer)");
    }

    private async Task<string> ReadSeedFileAsync(string fileName)
    {
        var path = Path.Combine(_seedDataPath, fileName);
        return await File.ReadAllTextAsync(path);
    }

    // ── Seed data DTOs ──────────────────────────────────────────────────

    private sealed class AgentSeedData
    {
        public string Name { get; set; } = string.Empty;
        public AgentRole Role { get; set; }
        public string Description { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
        public string Personality { get; set; } = string.Empty;
        public string? Backstory { get; set; }
        public List<string>? Expertise { get; set; }
        public List<string>? Values { get; set; }
        public List<string>? CommunicationQuirks { get; set; }
        public string? CommunicationStyle { get; set; }
        public ProviderCapability? PreferredProviderCapability { get; set; }
        public string? ProviderType { get; set; }
        public string? ModelName { get; set; }
        public string? Color { get; set; }
        public string? AvatarInitials { get; set; }
        public bool CanCreateArtifacts { get; set; }
        public bool CanTriggerAgents { get; set; }
        public bool CanExecuteCommands { get; set; }
        public bool VibeMode { get; set; }
        public int TimeoutSeconds { get; set; }
    }

    private sealed class TeamSeedData
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<string>? AgentNames { get; set; }
    }

    private sealed class TeamFlowSeedData
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int MaxReviewCycles { get; set; } = 3;
        public int MaxRetriesPerStep { get; set; } = 2;
        public bool RequireUserApprovalBeforeCoding { get; set; } = true;
        public bool RunTestsAfterReview { get; set; } = true;
        public string? DefaultCommunicationStyle { get; set; }
        public List<FlowStepSeedData>? Steps { get; set; }
    }

    private sealed class FlowStepSeedData
    {
        public int Order { get; set; }
        public string? AgentRole { get; set; }
        public string? Action { get; set; }
        public string? ReportsTo { get; set; }
        public string? Condition { get; set; }
        public bool IsOptional { get; set; }
        public string? ChatTemplate { get; set; }
        public int? NextStepOnSuccess { get; set; }
        public int? NextStepOnChangesRequested { get; set; }
        public ProviderCapability? RequiredCapability { get; set; }
    }

    private sealed class ProviderSeedData
    {
        public string Name { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public bool IsDetected { get; set; }
        public List<string>? AvailableModels { get; set; }
        public List<ProviderCapability>? Capabilities { get; set; }
        public string? StrengthDescription { get; set; }
        public ProviderCostTier CostTier { get; set; }
        public int Priority { get; set; } = 100;
    }
}
