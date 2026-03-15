using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace AiDevTeam.Infrastructure.Services;

public class AgentExecutionService : IAgentExecutionService
{
    private readonly IAgentPromptService _promptService;
    private readonly IAgentResponseProcessor _responseProcessor;
    private readonly IMessageService _messageService;
    private readonly IAgentDefinitionService _agentDefinitionService;
    private readonly IArtifactService _artifactService;
    private readonly StoragePaths _paths;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentExecutionService> _logger;

    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _runningTasks = new();
    private const int MaxChainDepth = 5;

    public AgentExecutionService(
        IAgentPromptService promptService,
        IAgentResponseProcessor responseProcessor,
        IMessageService messageService,
        IAgentDefinitionService agentDefinitionService,
        IArtifactService artifactService,
        StoragePaths paths,
        IServiceProvider serviceProvider,
        ILogger<AgentExecutionService> logger)
    {
        _promptService = promptService;
        _responseProcessor = responseProcessor;
        _messageService = messageService;
        _agentDefinitionService = agentDefinitionService;
        _artifactService = artifactService;
        _paths = paths;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<AgentRun> StartRunAsync(string conversationId, string agentDefinitionId, string prompt, int chainDepth = 0)
    {
        var agent = await _agentDefinitionService.GetByIdAsync(agentDefinitionId)
            ?? throw new InvalidOperationException($"Agent {agentDefinitionId} not found");

        var run = new AgentRun
        {
            ConversationId = conversationId,
            AgentDefinitionId = agentDefinitionId,
            AgentName = agent.Name,
            InputPrompt = prompt,
            Status = AgentRunStatus.Queued,
            ChainDepth = chainDepth
        };

        var runsFile = _paths.RunsFile(conversationId);
        Directory.CreateDirectory(_paths.ConversationDir(conversationId));
        await JsonStore.UpdateListAsync<AgentRun>(runsFile, list => { list.Add(run); return list; });

        var cts = new CancellationTokenSource();
        _runningTasks[run.Id] = cts;
        _ = Task.Run(async () => await ExecuteRunAsync(run.Id, conversationId, agent, prompt, chainDepth, cts.Token));

        return run;
    }

    public async Task<AgentRun> ExecuteRunToCompletionAsync(string conversationId, string agentDefinitionId, string prompt, CancellationToken ct = default, bool skipChatMessage = false)
    {
        var agent = await _agentDefinitionService.GetByIdAsync(agentDefinitionId)
            ?? throw new InvalidOperationException($"Agent {agentDefinitionId} not found");

        var run = new AgentRun
        {
            ConversationId = conversationId,
            AgentDefinitionId = agentDefinitionId,
            AgentName = agent.Name,
            InputPrompt = prompt,
            Status = AgentRunStatus.Queued,
            ChainDepth = 0
        };

        var runsFile = _paths.RunsFile(conversationId);
        Directory.CreateDirectory(_paths.ConversationDir(conversationId));
        await JsonStore.UpdateListAsync<AgentRun>(runsFile, list => { list.Add(run); return list; });

        _runningTasks[run.Id] = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Execute synchronously — await the full run before returning
        // When skipChatMessage is true, we skip posting the raw response to chat
        // (the workflow engine will compose a structured chat message instead)
        await ExecuteRunAsync(run.Id, conversationId, agent, prompt, 0, ct, skipChatMessage);

        // Re-read the completed run from disk to get the populated fields
        var completedRun = await GetByIdAsync(run.Id);
        return completedRun ?? run;
    }

    private async Task ExecuteRunAsync(string runId, string conversationId, AgentDefinition agent, string prompt, int chainDepth, CancellationToken ct, bool skipChatMessage = false)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            await UpdateRunStatus(conversationId, runId, AgentRunStatus.Running);

            // Validate agent configuration before execution
            var validationResult = await ValidateAgentConfiguration(agent);
            if (!validationResult.IsValid)
            {
                _logger.LogError("Agent configuration validation failed for {Agent}: {Error}", agent.Name, validationResult.Error);
                throw new InvalidOperationException($"Agent configuration invalid: {validationResult.Error}");
            }

            var providers = _serviceProvider.GetServices<IAgentProvider>();
            var provider = providers.FirstOrDefault(p => p.ProviderType == agent.ProviderType);

            if (provider == null)
            {
                var availableProviders = string.Join(", ", providers.Select(p => p.ProviderType));
                throw new InvalidOperationException($"Provider '{agent.ProviderType}' not found. Available providers: {availableProviders}");
            }

            // Build the prompt with team context and conversation history
            var allAgents = await GetConversationAgentsAsync(conversationId);
            var contextPrompt = await _promptService.BuildContextAwarePromptAsync(conversationId, agent, prompt);
            var systemPrompt = await _promptService.BuildSystemPromptAsync(conversationId, agent, allAgents);

            var request = new AgentRunRequest
            {
                Prompt = contextPrompt,
                SystemPrompt = systemPrompt,
                ModelName = agent.ModelName,
                ExecutablePath = agent.ExecutablePath,
                CommandTemplate = agent.CommandTemplate,
                DefaultArguments = agent.DefaultArguments,
                WorkingDirectory = _artifactService.GetArtifactDirectory(conversationId),
                TimeoutSeconds = agent.TimeoutSeconds,
                VibeMode = agent.VibeMode,
                AllowedTools = agent.AllowedTools,
                DisallowedTools = agent.DisallowedTools
            };

            _logger.LogInformation("Executing {Agent} with prompt length: {PromptLength}, system prompt length: {SystemPromptLength}",
                agent.Name, request.Prompt?.Length ?? 0, request.SystemPrompt?.Length ?? 0);

            var result = await provider.ExecuteAsync(request, ct);
            sw.Stop();

            // Enhanced result logging
            _logger.LogInformation("Agent {Agent} execution completed - Success: {Success}, Duration: {Duration}ms, Output length: {OutputLength}, Error: {HasError}",
                agent.Name, result.Success, sw.ElapsedMilliseconds, result.Output?.Length ?? 0, !string.IsNullOrEmpty(result.Error));

            // Update run
            await UpdateRun(conversationId, runId, r =>
            {
                r.Status = result.Success ? AgentRunStatus.Succeeded : AgentRunStatus.Failed;
                r.OutputText = result.Output;
                r.ErrorText = result.Error;
                r.ExitCode = result.ExitCode;
                r.CompletedAt = DateTime.UtcNow;
                r.DurationMs = sw.ElapsedMilliseconds;
            });

            if (result.Success)
            {
                var response = result.Output?.Trim() ?? "";

                if (!skipChatMessage)
                {
                    // Post response to chat and extract artifacts
                    await _responseProcessor.ProcessAgentResponseAsync(conversationId, agent, response, runId);
                }
                else
                {
                    // Workflow mode: only extract code artifacts, skip chat message
                    // (the workflow engine composes structured chat messages)
                    await _responseProcessor.ExtractAndSaveCodeArtifactsAsync(conversationId, agent, response, runId);
                }
            }
            else
            {
                // Enhanced error reporting
                var errorDetails = !string.IsNullOrEmpty(result.Error) ? result.Error : result.Output;
                var errorMessage = $"I encountered a problem and couldn't complete the task.\n\n**Error Details:**\n```\n{errorDetails}\n```";

                if (result.ExitCode != 0)
                    errorMessage += $"\n\n**Exit Code:** {result.ExitCode}";

                await _messageService.AddAsync(conversationId, agent.Name, agent.Role.ToString(),
                    MessageType.Blocker, errorMessage, agentRunId: runId);

                _logger.LogError("Agent {Agent} failed with exit code {ExitCode}: {Error}",
                    agent.Name, result.ExitCode, errorDetails);
            }

            // Handle explicit artifacts from the provider
            foreach (var art in result.Artifacts)
            {
                try
                {
                    var artifact = await _artifactService.CreateAsync(conversationId, art.FileName,
                        AgentTextUtilities.GetArtifactType(art.FileName), art.Content, agent.Name);
                    await _messageService.AddAsync(conversationId, agent.Name, agent.Role.ToString(),
                        MessageType.ArtifactCreated, $"Created **{art.FileName}**",
                        relatedArtifactId: artifact.Id, agentRunId: runId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create artifact {FileName} for agent {Agent}", art.FileName, agent.Name);
                }
            }

            // Update rolling context
            await _responseProcessor.UpdateAgentContextAsync(conversationId, agent, prompt, result.Output?.Trim() ?? "", result.Success);

            // Agent-to-agent chaining — ask orchestrator if anyone else should act next
            if (result.Success && agent.Role != AgentRole.Orchestrator && chainDepth < MaxChainDepth)
            {
                var routingService = _serviceProvider.GetRequiredService<IAgentRoutingService>();
                await routingService.RequestFollowUpRoutingAsync(conversationId, agent.Id, runId, result.Output ?? "", chainDepth);
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            await UpdateRun(conversationId, runId, r =>
            {
                r.Status = AgentRunStatus.Cancelled;
                r.CompletedAt = DateTime.UtcNow;
                r.DurationMs = sw.ElapsedMilliseconds;
            });
            await _messageService.AddAsync(conversationId, "System", "System",
                MessageType.StatusUpdate, $"**{agent.Name}**'s task was cancelled.", agentRunId: runId);
            _logger.LogInformation("Agent {Agent} task was cancelled", agent.Name);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Agent run {RunId} failed for agent {Agent}: {Error}", runId, agent.Name, ex.Message);

            await UpdateRun(conversationId, runId, r =>
            {
                r.Status = AgentRunStatus.Failed;
                r.ErrorText = ex.Message;
                r.CompletedAt = DateTime.UtcNow;
                r.DurationMs = sw.ElapsedMilliseconds;
            });

            // Provide helpful error message to user
            var userErrorMessage = ex.Message.Contains("not found") || ex.Message.Contains("configuration")
                ? $"**{agent.Name}** has a configuration issue: {ex.Message}\n\nPlease check the agent settings in the Agents page."
                : $"**{agent.Name}** encountered an unexpected error: {ex.Message}";

            await _messageService.AddAsync(conversationId, "System", "System",
                MessageType.Blocker, userErrorMessage, agentRunId: runId);
        }
        finally
        {
            _runningTasks.TryRemove(runId, out _);
        }
    }

    private async Task<(bool IsValid, string Error)> ValidateAgentConfiguration(AgentDefinition agent)
    {
        if (!agent.IsEnabled)
            return (false, "Agent is disabled");

        if (string.IsNullOrWhiteSpace(agent.Name))
            return (false, "Agent name is required");

        if (string.IsNullOrWhiteSpace(agent.ProviderType))
            return (false, "Provider type is required");

        if (agent.TimeoutSeconds <= 0 || agent.TimeoutSeconds > 1800)
            return (false, "Timeout must be between 1 and 1800 seconds");

        // Validate provider availability
        var providers = _serviceProvider.GetServices<IAgentProvider>();
        var provider = providers.FirstOrDefault(p => p.ProviderType == agent.ProviderType);
        if (provider == null)
        {
            var availableProviders = string.Join(", ", providers.Select(p => p.ProviderType));
            return (false, $"Provider '{agent.ProviderType}' not available. Available: {availableProviders}");
        }

        return (true, string.Empty);
    }

    public async Task<AgentRun?> GetByIdAsync(string id)
    {
        var dir = _paths.ConversationsDir;
        if (!Directory.Exists(dir)) return null;
        foreach (var convDir in Directory.GetDirectories(dir))
        {
            var convId = Path.GetFileName(convDir);
            var runs = await JsonStore.LoadListAsync<AgentRun>(_paths.RunsFile(convId));
            var run = runs.FirstOrDefault(r => r.Id == id);
            if (run != null) return run;
        }
        return null;
    }

    public async Task<List<AgentRun>> GetByConversationAsync(string conversationId)
    {
        var runs = await JsonStore.LoadListAsync<AgentRun>(_paths.RunsFile(conversationId));
        return runs.OrderByDescending(r => r.StartedAt).ToList();
    }

    public Task CancelRunAsync(string runId)
    {
        if (_runningTasks.TryGetValue(runId, out var cts))
            cts.Cancel();
        return Task.CompletedTask;
    }

    private async Task UpdateRunStatus(string conversationId, string runId, AgentRunStatus status)
    {
        await UpdateRun(conversationId, runId, r => r.Status = status);
    }

    private async Task UpdateRun(string conversationId, string runId, Action<AgentRun> update)
    {
        var runsFile = _paths.RunsFile(conversationId);
        await JsonStore.UpdateListAsync<AgentRun>(runsFile, list =>
        {
            var run = list.FirstOrDefault(r => r.Id == runId);
            if (run != null) update(run);
            return list;
        });
    }

    private async Task<List<AgentDefinition>> GetConversationAgentsAsync(string conversationId)
    {
        var allAgents = (await _agentDefinitionService.GetAllAsync()).Where(a => a.IsEnabled).ToList();

        var convService = _serviceProvider.GetRequiredService<IConversationService>();
        var conversation = await convService.GetByIdAsync(conversationId);
        if (conversation?.TeamId == null) return allAgents;

        var teamService = _serviceProvider.GetRequiredService<ITeamService>();
        var team = await teamService.GetByIdAsync(conversation.TeamId);
        if (team == null) return allAgents;

        return allAgents.Where(a => team.AgentIds.Contains(a.Id)).ToList();
    }
}
