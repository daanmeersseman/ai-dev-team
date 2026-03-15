using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AiDevTeam.Infrastructure.Services;

public class AgentRoutingService : IAgentRoutingService
{
    private readonly IAgentExecutionService _executionService;
    private readonly IAgentPromptService _promptService;
    private readonly IAgentResponseProcessor _responseProcessor;
    private readonly IMessageService _messageService;
    private readonly IAgentDefinitionService _agentDefinitionService;
    private readonly IConversationService _conversationService;
    private readonly ITeamService _teamService;
    private readonly IArtifactService _artifactService;
    private readonly StoragePaths _paths;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentRoutingService> _logger;

    private const int MaxChainDepth = 5;

    public AgentRoutingService(
        IAgentExecutionService executionService,
        IAgentPromptService promptService,
        IAgentResponseProcessor responseProcessor,
        IMessageService messageService,
        IAgentDefinitionService agentDefinitionService,
        IConversationService conversationService,
        ITeamService teamService,
        IArtifactService artifactService,
        StoragePaths paths,
        IServiceProvider serviceProvider,
        ILogger<AgentRoutingService> logger)
    {
        _executionService = executionService;
        _promptService = promptService;
        _responseProcessor = responseProcessor;
        _messageService = messageService;
        _agentDefinitionService = agentDefinitionService;
        _conversationService = conversationService;
        _teamService = teamService;
        _artifactService = artifactService;
        _paths = paths;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// The orchestrator analyzes the user message and returns a routing decision.
    /// It responds with its own message AND a ROUTE: directive telling us who to dispatch to.
    /// </summary>
    public async Task StartOrchestratorRoutingAsync(string conversationId, AgentDefinition orchestrator, List<AgentDefinition> allAgents, string userMessage)
    {
        var sw = Stopwatch.StartNew();

        var run = new AgentRun
        {
            ConversationId = conversationId,
            AgentDefinitionId = orchestrator.Id,
            AgentName = orchestrator.Name,
            InputPrompt = userMessage,
            Status = AgentRunStatus.Queued
        };

        var runsFile = _paths.RunsFile(conversationId);
        Directory.CreateDirectory(_paths.ConversationDir(conversationId));
        await JsonStore.UpdateListAsync<AgentRun>(runsFile, list => { list.Add(run); return list; });

        var cts = new CancellationTokenSource();

        // Run orchestrator in background
        _ = Task.Run(async () =>
        {
            try
            {
                await UpdateRunStatus(conversationId, run.Id, AgentRunStatus.Running);

                var providers = _serviceProvider.GetServices<IAgentProvider>();
                var provider = providers.FirstOrDefault(p => p.ProviderType == orchestrator.ProviderType)
                    ?? throw new InvalidOperationException($"Provider '{orchestrator.ProviderType}' not found");

                var contextPrompt = await _promptService.BuildContextAwarePromptAsync(conversationId, orchestrator, userMessage);
                var systemPrompt = await _promptService.BuildSystemPromptAsync(conversationId, orchestrator, allAgents);

                var request = new AgentRunRequest
                {
                    Prompt = contextPrompt,
                    SystemPrompt = systemPrompt,
                    ModelName = orchestrator.ModelName,
                    ExecutablePath = orchestrator.ExecutablePath,
                    CommandTemplate = orchestrator.CommandTemplate,
                    DefaultArguments = orchestrator.DefaultArguments,
                    WorkingDirectory = _artifactService.GetArtifactDirectory(conversationId),
                    TimeoutSeconds = orchestrator.TimeoutSeconds,
                    VibeMode = orchestrator.VibeMode
                };

                var result = await provider.ExecuteAsync(request, cts.Token);
                sw.Stop();

                await UpdateRun(conversationId, run.Id, r =>
                {
                    r.Status = result.Success ? AgentRunStatus.Succeeded : AgentRunStatus.Failed;
                    r.OutputText = result.Output;
                    r.ErrorText = result.Error;
                    r.ExitCode = result.ExitCode;
                    r.CompletedAt = DateTime.UtcNow;
                    r.DurationMs = sw.ElapsedMilliseconds;
                });

                if (!result.Success)
                {
                    await _messageService.AddAsync(conversationId, orchestrator.Name, orchestrator.Role.ToString(),
                        MessageType.Blocker,
                        $"I ran into a problem. **Error:** \n\n```\n{result.Error ?? result.Output}\n```",
                        agentRunId: run.Id);
                    return;
                }

                var response = result.Output?.Trim() ?? "";

                // Parse out ROUTE: directives with 3-tier fallback
                var (chatMessage, routeTargets, tier) = ParseRoutingResponse(response, allAgents);

                // Tier 3: no route found but delegation language present — re-prompt with enhanced validation
                if (tier == 3 && AgentTextUtilities.ContainsDelegationLanguage(response))
                {
                    _logger.LogInformation("Tier 3 re-prompt: delegation language detected, asking for explicit ROUTE");

                    var availableAgentNames = string.Join(", ", allAgents
                        .Where(a => a.Role != AgentRole.Orchestrator && a.IsEnabled)
                        .Select(a => a.Name.Split(' ', '(')[0].Trim()));

                    var rePromptRequest = new AgentRunRequest
                    {
                        Prompt = $"You said:\n\"{response}\"\n\nBased on this, which team members should act? Available agents: {availableAgentNames}\n\n" +
                                "CRITICAL: Reply with ONLY a ROUTE: line using exact names. Examples:\n" +
                                "- ROUTE: Sam, Morgan\n" +
                                "- ROUTE: all\n" +
                                "- ROUTE: none\n\n" +
                                "Reply with ONLY the ROUTE: line, nothing else:",
                        SystemPrompt = "You are Alex the orchestrator. Reply with only a ROUTE: directive line using exact agent names. Nothing else. Use format: ROUTE: Name1, Name2",
                        ModelName = orchestrator.ModelName,
                        ExecutablePath = orchestrator.ExecutablePath,
                        CommandTemplate = orchestrator.CommandTemplate,
                        DefaultArguments = orchestrator.DefaultArguments,
                        WorkingDirectory = _artifactService.GetArtifactDirectory(conversationId),
                        TimeoutSeconds = 30,
                        VibeMode = orchestrator.VibeMode
                    };

                    var reResult = await provider.ExecuteAsync(rePromptRequest, cts.Token);
                    if (reResult.Success && !string.IsNullOrWhiteSpace(reResult.Output))
                    {
                        _logger.LogInformation("Re-prompt response: '{Response}'", reResult.Output.Trim());
                        var (_, reTargets, reTier) = ParseRoutingResponse(reResult.Output.Trim(), allAgents);
                        if (reTargets.Any())
                        {
                            routeTargets = reTargets;
                            _logger.LogInformation("Tier 3 re-prompt succeeded (tier {Tier}): {Targets}", reTier, string.Join(", ", reTargets.Select(t => t.Name)));
                        }
                        else
                        {
                            _logger.LogWarning("Tier 3 re-prompt failed to produce valid targets. Response: '{Response}'", reResult.Output.Trim());
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Tier 3 re-prompt failed. Success: {Success}, Output: '{Output}', Error: '{Error}'",
                            reResult.Success, reResult.Output, reResult.Error);
                    }
                }

                // Post the orchestrator's full chat message (without the ROUTE: lines)
                if (!string.IsNullOrWhiteSpace(chatMessage))
                {
                    await _messageService.AddAsync(conversationId, orchestrator.Name, orchestrator.Role.ToString(),
                        MessageType.Plan, chatMessage, agentRunId: run.Id);
                }

                // Dispatch to routed agents — with visible handoff messages
                foreach (var target in routeTargets)
                {
                    _logger.LogInformation("Orchestrator dispatching to {Agent}", target.Name);

                    await Task.Delay(300);

                    var delegationPrompt = $"The user said: \"{userMessage}\"\n\n" +
                        (string.IsNullOrWhiteSpace(chatMessage) ? "" : $"{orchestrator.Name} says: \"{chatMessage}\"\n\n") +
                        $"Please respond as {target.Name} based on your expertise.";

                    await _executionService.StartRunAsync(conversationId, target.Id, delegationPrompt);
                }

                await _responseProcessor.UpdateAgentContextAsync(conversationId, orchestrator, userMessage, result.Output ?? "", result.Success);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                await UpdateRun(conversationId, run.Id, r =>
                {
                    r.Status = AgentRunStatus.Cancelled;
                    r.CompletedAt = DateTime.UtcNow;
                    r.DurationMs = sw.ElapsedMilliseconds;
                });
                _logger.LogInformation("Orchestrator routing was cancelled");
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Orchestrator routing failed for conversation {ConversationId}: {Error}", conversationId, ex.Message);
                await UpdateRun(conversationId, run.Id, r =>
                {
                    r.Status = AgentRunStatus.Failed;
                    r.ErrorText = ex.Message;
                    r.CompletedAt = DateTime.UtcNow;
                    r.DurationMs = sw.ElapsedMilliseconds;
                });

                // Provide helpful error message and attempt fallback routing
                var errorMsg = ex.Message.Contains("not found") || ex.Message.Contains("Provider")
                    ? $"Alex (orchestrator) has a configuration issue: {ex.Message}\n\nPlease check the agent settings."
                    : $"Alex encountered an error while analyzing your request: {ex.Message}";

                await _messageService.AddAsync(conversationId, "System", "System",
                    MessageType.Blocker, errorMsg, agentRunId: run.Id);

                // Attempt fallback routing to available agents if it's not a configuration issue
                if (!ex.Message.Contains("not found") && !ex.Message.Contains("Provider"))
                {
                    try
                    {
                        _logger.LogInformation("Attempting fallback routing after orchestrator failure");
                        await AttemptFallbackRoutingAsync(conversationId, userMessage, allAgents);
                    }
                    catch (Exception fallbackEx)
                    {
                        _logger.LogWarning(fallbackEx, "Fallback routing also failed");
                    }
                }
            }
        });
    }

    /// <summary>
    /// Parse the orchestrator's response with 3-tier fallback and enhanced validation:
    ///   Tier 1: Explicit ROUTE: directive
    ///   Tier 2: @mentions in the response text (e.g. @Sam, @Morgan)
    ///   Tier 3: No route found — returns empty (caller can re-prompt if delegation language present)
    /// </summary>
    private (string chatMessage, List<AgentDefinition> targets, int tier) ParseRoutingResponse(string response, List<AgentDefinition> allAgents)
    {
        var targets = new List<AgentDefinition>();
        var chatLines = new List<string>();
        var foundRoute = false;

        if (string.IsNullOrWhiteSpace(response))
        {
            _logger.LogWarning("Empty response received from orchestrator");
            return (string.Empty, targets, 3);
        }

        var lines = response.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Tier 1: Look for ROUTE: directive
            if (trimmed.StartsWith("ROUTE:", StringComparison.OrdinalIgnoreCase))
            {
                foundRoute = true;
                var routeContent = trimmed.Substring(6).Trim();

                _logger.LogInformation("Found ROUTE directive: '{RouteContent}'", routeContent);

                // Handle special cases
                if (string.IsNullOrWhiteSpace(routeContent) ||
                    routeContent.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("ROUTE directive indicates no routing needed");
                    break;
                }

                var names = routeContent.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var name in names)
                {
                    var nameLower = name.ToLowerInvariant().Trim();
                    if (nameLower == "all" || nameLower == "everyone")
                    {
                        targets = allAgents.Where(a => a.Role != AgentRole.Orchestrator && a.IsEnabled).ToList();
                        _logger.LogInformation("ROUTE: all - dispatching to {Count} agents", targets.Count);
                        break;
                    }

                    var match = FindAgentByName(name.Trim(), allAgents);
                    if (match != null && !targets.Contains(match))
                    {
                        targets.Add(match);
                        _logger.LogInformation("Matched agent '{Name}' to '{AgentName}'", name, match.Name);
                    }
                    else if (match == null)
                    {
                        _logger.LogWarning("Could not find agent matching name '{Name}'. Available agents: {Agents}",
                            name, string.Join(", ", allAgents.Where(a => a.Role != AgentRole.Orchestrator).Select(a => a.Name)));
                    }
                }
            }
            else
            {
                chatLines.Add(line);
            }
        }

        var chatMessage = string.Join("\n", chatLines).Trim();

        if (foundRoute && targets.Any())
        {
            _logger.LogInformation("Routing tier 1 (ROUTE directive): {Targets}", string.Join(", ", targets.Select(t => t.Name)));
            return (chatMessage, targets, 1);
        }

        if (foundRoute && !targets.Any())
        {
            _logger.LogWarning("ROUTE directive found but no valid agents matched");
        }

        // Tier 2: Parse @mentions from full response
        var mentionTargets = ParseAtMentions(response, allAgents);
        if (mentionTargets.Any())
        {
            _logger.LogInformation("Routing tier 2 (@mentions): {Targets}", string.Join(", ", mentionTargets.Select(t => t.Name)));
            return (chatMessage, mentionTargets, 2);
        }

        // Tier 3: No routing found
        _logger.LogInformation("Routing tier 3: no ROUTE directive or @mentions found");
        return (chatMessage, targets, 3);
    }

    /// <summary>
    /// Enhanced agent name matching with fuzzy matching and alias support.
    /// </summary>
    private static AgentDefinition? FindAgentByName(string name, List<AgentDefinition> allAgents)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var nameToMatch = name.Trim();
        var availableAgents = allAgents.Where(a => a.Role != AgentRole.Orchestrator && a.IsEnabled).ToList();

        // Exact first name match (case insensitive)
        var exactMatch = availableAgents.FirstOrDefault(a =>
        {
            var firstName = a.Name.Split(' ', '(')[0].Trim();
            return firstName.Equals(nameToMatch, StringComparison.OrdinalIgnoreCase);
        });
        if (exactMatch != null) return exactMatch;

        // Full name match
        var fullNameMatch = availableAgents.FirstOrDefault(a =>
            a.Name.Equals(nameToMatch, StringComparison.OrdinalIgnoreCase));
        if (fullNameMatch != null) return fullNameMatch;

        // Role-based match (e.g., "coder", "tester")
        if (Enum.TryParse<AgentRole>(nameToMatch, true, out var role))
        {
            var roleMatch = availableAgents.FirstOrDefault(a => a.Role == role);
            if (roleMatch != null) return roleMatch;
        }

        // Partial name match (contains)
        var partialMatch = availableAgents.FirstOrDefault(a =>
            a.Name.Contains(nameToMatch, StringComparison.OrdinalIgnoreCase));
        if (partialMatch != null) return partialMatch;

        return null;
    }

    /// <summary>
    /// Parse @Name mentions from response text (Tier 2 fallback).
    /// </summary>
    private static List<AgentDefinition> ParseAtMentions(string response, List<AgentDefinition> allAgents)
    {
        var targets = new List<AgentDefinition>();
        var matches = Regex.Matches(response, @"@(\w+)");
        foreach (Match m in matches)
        {
            var mentioned = m.Groups[1].Value;
            var agent = allAgents.FirstOrDefault(a =>
            {
                var firstName = a.Name.Split(' ', '(')[0].Trim();
                return firstName.Equals(mentioned, StringComparison.OrdinalIgnoreCase)
                    && a.Role != AgentRole.Orchestrator;
            });
            if (agent != null && !targets.Contains(agent))
                targets.Add(agent);
        }
        return targets;
    }

    /// <summary>
    /// If user message starts with an agent's first name (e.g. "Morgan review it"),
    /// return that agent for direct dispatch. Only matches message-start to avoid false positives.
    /// </summary>
    public AgentDefinition? TryMatchDirectMention(string message, List<AgentDefinition> agents)
    {
        var trimmed = message.TrimStart();
        foreach (var agent in agents.Where(a => a.Role != AgentRole.Orchestrator))
        {
            var firstName = agent.Name.Split(' ', '(')[0].Trim();
            if (trimmed.StartsWith(firstName, StringComparison.OrdinalIgnoreCase)
                && (trimmed.Length == firstName.Length
                    || !char.IsLetterOrDigit(trimmed[firstName.Length])))
            {
                return agent;
            }
        }
        return null;
    }

    /// <summary>
    /// After a non-orchestrator agent completes, ask the orchestrator if anyone should act next.
    /// Uses the same 3-tier routing fallback. Bounded by MaxChainDepth.
    /// When nobody is next (or max depth hit), triggers an orchestrator summary.
    /// </summary>
    public async Task RequestFollowUpRoutingAsync(string conversationId, string completedAgentId, string runId, string output, int currentChainDepth)
    {
        try
        {
            var completedAgent = await _agentDefinitionService.GetByIdAsync(completedAgentId);
            if (completedAgent == null) return;

            var agents = await GetConversationAgentsAsync(conversationId);
            var orchestrator = agents.FirstOrDefault(a => a.Role == AgentRole.Orchestrator);
            if (orchestrator == null) return;

            // Check if there are sibling agents still running at the same chain depth
            // If so, defer — let the last one to finish trigger the follow-up routing
            var allRuns = await _executionService.GetByConversationAsync(conversationId);
            var siblingsStillRunning = allRuns.Any(r =>
                r.ChainDepth == currentChainDepth &&
                r.AgentName != completedAgent.Name &&
                (r.Status == AgentRunStatus.Running || r.Status == AgentRunStatus.Queued));

            if (siblingsStillRunning)
            {
                _logger.LogInformation("Parallel agents still running at depth {Depth}, deferring follow-up routing", currentChainDepth);
                return;
            }

            var providers = _serviceProvider.GetServices<IAgentProvider>();
            var provider = providers.FirstOrDefault(p => p.ProviderType == orchestrator.ProviderType);
            if (provider == null) return;

            var nextDepth = currentChainDepth + 1;

            // At max depth, skip routing and go straight to summary
            if (nextDepth >= MaxChainDepth)
            {
                _logger.LogInformation("Max chain depth reached ({Depth}), generating summary", nextDepth);
                await GenerateOrchestratorSummary(conversationId, orchestrator, provider);
                return;
            }

            // Reload runs to get the latest state (siblings may have completed)
            var existingRuns = await _executionService.GetByConversationAsync(conversationId);

            // Gather ALL completed outputs at this depth (not just the last agent's)
            var completedAtThisDepth = existingRuns
                .Where(r => r.ChainDepth == currentChainDepth && r.Status == AgentRunStatus.Succeeded)
                .OrderBy(r => r.StartedAt)
                .ToList();

            // Build a combined view of what all parallel agents produced
            string outputForRouting;
            if (completedAtThisDepth.Count > 1)
            {
                var parts = completedAtThisDepth.Select(r =>
                {
                    var text = r.OutputText?.Length > 600 ? r.OutputText[..600] + "..." : (r.OutputText ?? "");
                    return $"**{r.AgentName}:**\n{text}";
                });
                outputForRouting = string.Join("\n\n---\n\n", parts);
            }
            else
            {
                outputForRouting = output.Length > 1000 ? output[..1000] + "\n...(truncated)" : output;
            }

            // Build chain history so the router knows what's already happened
            var chainHistory = existingRuns
                .Where(r => r.Status == AgentRunStatus.Succeeded && r.AgentName != orchestrator.Name)
                .OrderBy(r => r.StartedAt)
                .Select(r => $"  {r.ChainDepth}. {r.AgentName} ({(r.DurationMs.HasValue ? $"{r.DurationMs}ms" : "?")}): {AgentTextUtilities.TruncateToOneLine(r.OutputText ?? "", 120)}")
                .ToList();

            var teamNames = string.Join(", ", agents
                .Where(a => a.Role != AgentRole.Orchestrator)
                .Select(a => $"{a.Name.Split(' ', '(')[0].Trim()} ({a.Role})"));

            var parallelNote = completedAtThisDepth.Count > 1
                ? $"NOTE: {completedAtThisDepth.Count} agents ran in parallel at depth {currentChainDepth}. All have finished. Their combined output is below.\n\n"
                : "";

            var followUpPrompt = $"{parallelNote}" +
                $"Latest output (depth {currentChainDepth}):\n\"\"\"\n{outputForRouting}\n\"\"\"\n\n" +
                $"## Full chain history:\n{string.Join("\n", chainHistory)}\n\n" +
                $"Available team members: {teamNames}\n\n" +
                "Based on the chain history and latest output, who should act NEXT?\n" +
                "IMPORTANT RULES:\n" +
                $"- Do NOT route back to {completedAgent.Name.Split(' ', '(')[0].Trim()} — they just finished\n" +
                "- The standard dev workflow is: code → review → test → done\n" +
                "- If the coder wrote code but it hasn't been REVIEWED yet, route to the reviewer\n" +
                "- If the reviewer approved, route to the tester to write and run actual tests\n" +
                "- If the reviewer had issues, route back to the coder to fix them\n" +
                "- If the tester only PLANNED tests but didn't write them, route BACK to the tester\n" +
                "- If someone asked a question, route to the person they asked\n" +
                "- If the coder answered a question, route back to the asker to continue their work\n" +
                "- If tests are complete and passed, THEN route to none\n" +
                "- If tests failed, route to the coder to fix failures\n" +
                "- Only say ROUTE: none when ALL phases are done: code written, reviewed, AND tested\n\n" +
                "Reply with ROUTE: Name or ROUTE: none";

            var request = new AgentRunRequest
            {
                Prompt = followUpPrompt,
                SystemPrompt = "You are the team router. Based on the chain history and what just completed, decide who acts next. Reply ONLY with a ROUTE: line. Think about whether the workflow is truly complete: has code been written, reviewed, AND tested?",
                ModelName = orchestrator.ModelName,
                ExecutablePath = orchestrator.ExecutablePath,
                CommandTemplate = orchestrator.CommandTemplate,
                DefaultArguments = orchestrator.DefaultArguments,
                WorkingDirectory = _artifactService.GetArtifactDirectory(conversationId),
                TimeoutSeconds = 30,
                VibeMode = orchestrator.VibeMode
            };

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await provider.ExecuteAsync(request, cts.Token);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            {
                await GenerateOrchestratorSummary(conversationId, orchestrator, provider);
                return;
            }

            var response = result.Output.Trim();

            // Check for explicit "none"
            if (response.Contains("ROUTE: none", StringComparison.OrdinalIgnoreCase)
                || response.Contains("ROUTE:none", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Follow-up routing: workflow complete — generating summary");
                await GenerateOrchestratorSummary(conversationId, orchestrator, provider);
                return;
            }

            var (_, followUpTargets, tier) = ParseRoutingResponse(response, agents);

            // Prevent self-routing — agent shouldn't be dispatched back to itself
            followUpTargets = followUpTargets.Where(t => t.Id != completedAgent.Id).ToList();

            if (!followUpTargets.Any())
            {
                _logger.LogInformation("Follow-up routing: no targets parsed — generating summary");
                await GenerateOrchestratorSummary(conversationId, orchestrator, provider);
                return;
            }

            _logger.LogInformation("Chaining to {Targets} at depth {Depth} (tier {Tier})",
                string.Join(", ", followUpTargets.Select(t => t.Name)), nextDepth, tier);

            // Truncate output for delegation but keep enough for the next agent to act on
            var outputForChain = output.Length > 2000 ? output[..2000] + "\n...(truncated)" : output;

            foreach (var target in followUpTargets)
            {
                await Task.Delay(300);
                var chainPrompt = $"{completedAgent.Name} ({completedAgent.Role}) just completed their work.\n\n" +
                    $"## {completedAgent.Name}'s output:\n{outputForChain}\n\n" +
                    $"As {target.Name}, please review the above and take appropriate action. " +
                    $"If it contains feedback, suggestions, or issues — address them specifically. " +
                    $"If they asked you a question, answer it and then do the actual work.";
                await _executionService.StartRunAsync(conversationId, target.Id, chainPrompt, nextDepth);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Follow-up routing failed after agent {AgentId} completed", completedAgentId);
        }
    }

    /// <summary>
    /// Validate workflow completeness and either dispatch missing steps or generate summary.
    /// </summary>
    private async Task GenerateOrchestratorSummary(string conversationId, AgentDefinition orchestrator, IAgentProvider provider)
    {
        try
        {
            var runs = await _executionService.GetByConversationAsync(conversationId);
            var agents = await GetConversationAgentsAsync(conversationId);

            var completedRuns = runs
                .Where(r => r.Status == AgentRunStatus.Succeeded && r.AgentName != orchestrator.Name)
                .ToList();

            // Check which roles have actually acted
            var rolesCompleted = completedRuns
                .Select(r => agents.FirstOrDefault(a => a.Name == r.AgentName)?.Role)
                .Where(r => r != null)
                .Distinct()
                .ToHashSet();

            var hasCoder = rolesCompleted.Contains(AgentRole.Coder);
            var hasReviewer = rolesCompleted.Contains(AgentRole.Reviewer);
            var hasTester = rolesCompleted.Contains(AgentRole.Tester);

            // Check if key workflow steps are missing and dispatch them
            var maxDepth = runs.Any() ? runs.Max(r => r.ChainDepth) : 0;
            if (maxDepth < MaxChainDepth - 1) // Only dispatch if we have depth budget
            {
                AgentDefinition? missingAgent = null;
                string? dispatchReason = null;

                if (hasCoder && !hasReviewer)
                {
                    missingAgent = agents.FirstOrDefault(a => a.Role == AgentRole.Reviewer);
                    dispatchReason = "Code was written but never reviewed";
                }
                else if (hasCoder && hasReviewer && !hasTester)
                {
                    missingAgent = agents.FirstOrDefault(a => a.Role == AgentRole.Tester);
                    dispatchReason = "Code was written and reviewed but never tested";
                }

                if (missingAgent != null)
                {
                    _logger.LogInformation("Workflow gap detected: {Reason}. Dispatching {Agent}", dispatchReason, missingAgent.Name);

                    var toName = missingAgent.Name.Split(' ', '(')[0].Trim();

                    // Build context from the latest coder output
                    var latestCoderRun = completedRuns
                        .Where(r => agents.FirstOrDefault(a => a.Name == r.AgentName)?.Role == AgentRole.Coder)
                        .OrderByDescending(r => r.StartedAt)
                        .FirstOrDefault();
                    var context = latestCoderRun?.OutputText ?? "";
                    var contextTrunc = context.Length > 2000 ? context[..2000] + "..." : context;

                    var chainPrompt = $"The team has been working on a task. Here's the latest code output:\n\n{contextTrunc}\n\n" +
                        $"As {missingAgent.Name}, please do your job: {dispatchReason!.ToLowerInvariant()}.";
                    await _executionService.StartRunAsync(conversationId, missingAgent.Id, chainPrompt, maxDepth + 1);
                    return; // Don't summarize yet — the missing step will trigger another summary attempt
                }
            }

            // All steps done (or we're at max depth) — generate the actual summary
            var messages = await _messageService.GetByConversationAsync(conversationId);
            var recentMessages = messages
                .Where(m => m.Type != MessageType.StatusUpdate && m.Type != MessageType.SystemEvent)
                .TakeLast(12)
                .Select(m => $"{m.Sender}: {AgentTextUtilities.TruncateToOneLine(m.Content, 150)}")
                .ToList();

            var runSummary = completedRuns
                .OrderBy(r => r.StartedAt)
                .Select(r => $"- {r.AgentName}: {AgentTextUtilities.TruncateToOneLine(r.OutputText ?? "", 100)}")
                .ToList();

            var workflowStatus = $"Coder: {(hasCoder ? "yes" : "NO")}, Reviewer: {(hasReviewer ? "yes" : "NO")}, Tester: {(hasTester ? "yes" : "NO")}";

            var summaryPrompt = "The team has finished. Give the user a brief recap — 3-5 sentences max, like a Slack update.\n\n" +
                $"## Workflow phases completed: {workflowStatus}\n" +
                $"## What happened:\n{string.Join("\n", runSummary)}\n\n" +
                $"## Recent chat:\n{string.Join("\n", recentMessages)}\n\n" +
                "Cover: what was delivered, any notable decisions or issues, and whether it's done or needs more work. Be concise and natural.";

            var request = new AgentRunRequest
            {
                Prompt = summaryPrompt,
                SystemPrompt = "You are Alex the Tech Lead. Give a brief, natural recap to the user. 3-5 sentences. No markdown headers. Just a casual team update like you'd post in Slack.",
                ModelName = orchestrator.ModelName,
                ExecutablePath = orchestrator.ExecutablePath,
                CommandTemplate = orchestrator.CommandTemplate,
                DefaultArguments = orchestrator.DefaultArguments,
                WorkingDirectory = _artifactService.GetArtifactDirectory(conversationId),
                TimeoutSeconds = 60,
                VibeMode = orchestrator.VibeMode
            };

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var result = await provider.ExecuteAsync(request, cts.Token);

            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
            {
                var summaryText = result.Output.Trim();
                await _messageService.AddAsync(conversationId, orchestrator.Name, orchestrator.Role.ToString(),
                    MessageType.Plan, $"📋 {summaryText}");

                _logger.LogInformation("Orchestrator summary posted for conversation {Id}", conversationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate orchestrator summary for {ConversationId}", conversationId);
        }
    }

    /// <summary>
    /// Attempt fallback routing when orchestrator fails, using simple heuristics.
    /// </summary>
    public async Task AttemptFallbackRoutingAsync(string conversationId, string userMessage, List<AgentDefinition> agents)
    {
        try
        {
            _logger.LogInformation("Attempting fallback routing based on message content analysis");

            var availableAgents = agents.Where(a => a.Role != AgentRole.Orchestrator && a.IsEnabled).ToList();
            if (!availableAgents.Any())
            {
                _logger.LogWarning("No available agents for fallback routing");
                return;
            }

            // Simple keyword-based routing
            var messageLower = userMessage.ToLowerInvariant();
            var fallbackTargets = new List<AgentDefinition>();

            // Look for coding-related keywords
            if (messageLower.Contains("code") || messageLower.Contains("implement") ||
                messageLower.Contains("build") || messageLower.Contains("create") ||
                messageLower.Contains("develop") || messageLower.Contains("fix"))
            {
                var coder = availableAgents.FirstOrDefault(a => a.Role == AgentRole.Coder);
                if (coder != null) fallbackTargets.Add(coder);
            }

            // Look for testing-related keywords
            if (messageLower.Contains("test") || messageLower.Contains("verify") ||
                messageLower.Contains("check") || messageLower.Contains("validate"))
            {
                var tester = availableAgents.FirstOrDefault(a => a.Role == AgentRole.Tester);
                if (tester != null && !fallbackTargets.Contains(tester)) fallbackTargets.Add(tester);
            }

            // Look for review-related keywords
            if (messageLower.Contains("review") || messageLower.Contains("analyze") ||
                messageLower.Contains("examine") || messageLower.Contains("look at"))
            {
                var reviewer = availableAgents.FirstOrDefault(a => a.Role == AgentRole.Reviewer);
                if (reviewer != null && !fallbackTargets.Contains(reviewer)) fallbackTargets.Add(reviewer);
            }

            // If no specific match, route to the first available agent
            if (!fallbackTargets.Any())
            {
                fallbackTargets.Add(availableAgents.First());
                _logger.LogInformation("No keyword matches, routing to first available agent: {Agent}", availableAgents.First().Name);
            }

            // Notify about fallback routing
            var targetNames = string.Join(", ", fallbackTargets.Select(t => t.Name));
            await _messageService.AddAsync(conversationId, "System", "System", MessageType.Plan,
                $"🔄 Routing directly to {targetNames} (orchestrator unavailable)");

            // Dispatch to selected agents
            foreach (var target in fallbackTargets)
            {
                await Task.Delay(500); // Small delay between dispatches
                _logger.LogInformation("Fallback routing to {Agent}", target.Name);
                var fallbackPrompt = $"The orchestrator is unavailable, so I'm routing this directly to you.\n\nUser request: {userMessage}";
                await _executionService.StartRunAsync(conversationId, target.Id, fallbackPrompt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallback routing failed: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Extract first 3 lines for handoff messages.
    /// </summary>
    private static string ExtractChatSummary(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var summary = new List<string>();
        foreach (var line in lines)
        {
            if (summary.Count > 0 && (line.StartsWith("##") || line.StartsWith("```")))
                break;
            if (!line.StartsWith("# "))
                summary.Add(line);
            if (summary.Count >= 3) break;
        }
        return summary.Any() ? string.Join("\n", summary).Trim() : "Here's my analysis.";
    }

    /// <summary>
    /// Load agents for a conversation, filtered by team if one is assigned.
    /// </summary>
    private async Task<List<AgentDefinition>> GetConversationAgentsAsync(string conversationId)
    {
        var allAgents = (await _agentDefinitionService.GetAllAsync()).Where(a => a.IsEnabled).ToList();

        var conversation = await _conversationService.GetByIdAsync(conversationId);
        if (conversation?.TeamId == null) return allAgents;

        var team = await _teamService.GetByIdAsync(conversation.TeamId);
        if (team == null) return allAgents;

        return allAgents.Where(a => team.AgentIds.Contains(a.Id)).ToList();
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
}
