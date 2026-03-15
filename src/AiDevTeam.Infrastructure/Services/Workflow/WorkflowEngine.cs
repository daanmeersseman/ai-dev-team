using System.Text.Json;
using System.Text.Json.Serialization;
using AiDevTeam.Core.Contracts;
using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Core.Models.Workflow;
using Microsoft.Extensions.Logging;

namespace AiDevTeam.Infrastructure.Services.Workflow;

public class WorkflowEngine : IWorkflowEngine
{
    private readonly IWorkflowStateMachine _stateMachine;
    private readonly IWorkflowExecutionService _executionService;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IOutputParser _outputParser;
    private readonly IConversationService _conversationService;
    private readonly IMessageService _messageService;
    private readonly IArtifactService _artifactService;
    private readonly IAgentDefinitionService _agentService;
    private readonly IAppSettingsService _settingsService;
    private readonly IAgentRunService _agentRunService;
    private readonly IContextBlockService _contextBlockService;
    private readonly IProviderConfigurationService _providerService;
    private readonly IChatMessageComposer _composer;
    private readonly ILogger<WorkflowEngine> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public event Func<string, WorkflowChatMessage, Task>? OnChatMessage;
    public event Func<string, WorkflowState, Task>? OnStateChanged;

    public WorkflowEngine(
        IWorkflowStateMachine stateMachine,
        IWorkflowExecutionService executionService,
        IPromptBuilder promptBuilder,
        IOutputParser outputParser,
        IConversationService conversationService,
        IMessageService messageService,
        IArtifactService artifactService,
        IAgentDefinitionService agentService,
        IAppSettingsService settingsService,
        IAgentRunService agentRunService,
        IContextBlockService contextBlockService,
        IProviderConfigurationService providerService,
        IChatMessageComposer composer,
        ILogger<WorkflowEngine> logger)
    {
        _stateMachine = stateMachine;
        _executionService = executionService;
        _promptBuilder = promptBuilder;
        _outputParser = outputParser;
        _conversationService = conversationService;
        _messageService = messageService;
        _artifactService = artifactService;
        _agentService = agentService;
        _settingsService = settingsService;
        _agentRunService = agentRunService;
        _contextBlockService = contextBlockService;
        _providerService = providerService;
        _composer = composer;
        _logger = logger;
    }

    public async Task<WorkflowExecution> StartWorkflowAsync(string conversationId, CancellationToken ct = default)
    {
        var conversation = await _conversationService.GetByIdAsync(conversationId)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found");

        var settings = await _settingsService.GetAsync();
        var flow = settings.TeamFlow;

        var execution = new WorkflowExecution
        {
            ConversationId = conversationId,
            TeamId = conversation.TeamId,
            FlowConfigurationName = flow.Name,
            CurrentState = WorkflowState.Created
        };

        await _executionService.SaveAsync(execution);

        // Link workflow to conversation
        conversation.WorkflowExecutionId = execution.Id;
        await _conversationService.UpdateAsync(conversation);

        _logger.LogInformation("Started workflow {WorkflowId} for conversation {ConversationId}", execution.Id, conversationId);

        // Immediately transition to Analyzing
        return await TransitionAndExecuteAsync(execution, WorkflowState.Analyzing, "Workflow started", ct);
    }

    public async Task<WorkflowExecution> ContinueWorkflowAsync(string conversationId, string? userInput = null, CancellationToken ct = default)
    {
        var execution = await _executionService.GetByConversationAsync(conversationId)
            ?? throw new InvalidOperationException($"No workflow found for conversation {conversationId}");

        if (execution.CurrentState == WorkflowState.WaitingForInput && userInput != null)
        {
            // Determine where we came from to route correctly
            var lastTransition = execution.Decisions.LastOrDefault(d => d.Type == WorkflowDecisionType.StateTransition);
            var cameFromReview = lastTransition?.FromState == WorkflowState.Reviewing;
            var cameFromPlanned = lastTransition?.ToState == WorkflowState.Planned
                || execution.Decisions.Any(d => d.Reason == "Awaiting user approval before coding");

            if (cameFromReview)
            {
                var wantsSuggestions = userInput.Trim().StartsWith("yes", StringComparison.OrdinalIgnoreCase)
                    || userInput.Trim().StartsWith("ja", StringComparison.OrdinalIgnoreCase);

                execution.PendingQuestions.Clear();
                RecordDecision(execution, WorkflowDecisionType.QuestionAnswered, "User",
                    wantsSuggestions ? "User wants suggestions implemented" : "User skipped suggestions");

                if (wantsSuggestions)
                {
                    // Route back to coding with suggestions as context
                    return await TransitionAndExecuteAsync(execution, WorkflowState.Coding,
                        "Implementing reviewer suggestions", ct, userInput);
                }
                else
                {
                    // Skip suggestions, proceed to testing
                    return await TransitionAndExecuteAsync(execution, WorkflowState.Testing,
                        "User skipped suggestions, proceeding to testing", ct);
                }
            }

            if (cameFromPlanned)
            {
                // User approved the plan — proceed to coding
                execution.PendingQuestions.Clear();
                RecordDecision(execution, WorkflowDecisionType.QuestionAnswered, "User",
                    $"User approved plan: {Truncate(userInput, 200)}");
                return await TransitionAndExecuteAsync(execution, WorkflowState.Coding,
                    "Plan approved by user, starting implementation", ct);
            }

            // Default: user answered questions or provided feedback
            execution.PendingQuestions.Clear();
            execution.PendingOptions.Clear();

            RecordDecision(execution, WorkflowDecisionType.QuestionAnswered, "User", $"User responded: {Truncate(userInput, 200)}");

            // Re-analyze with user feedback
            return await TransitionAndExecuteAsync(execution, WorkflowState.Analyzing,
                $"User provided input: {Truncate(userInput, 100)}", ct, userInput);
        }

        // Continue the workflow based on current state
        return await ExecuteCurrentStateAsync(execution, ct);
    }

    public async Task<WorkflowExecution> SelectOptionAsync(string conversationId, int optionNumber, CancellationToken ct = default)
    {
        var execution = await _executionService.GetByConversationAsync(conversationId)
            ?? throw new InvalidOperationException($"No workflow found for conversation {conversationId}");

        var option = execution.PendingOptions.FirstOrDefault(o => o.Number == optionNumber);
        var feedback = option != null
            ? $"Ik kies optie {optionNumber}: {option.Title}"
            : $"Ik kies optie {optionNumber}";

        execution.PendingOptions.Clear();
        RecordDecision(execution, WorkflowDecisionType.OptionSelected, "User", feedback);

        return await TransitionAndExecuteAsync(execution, WorkflowState.Analyzing, feedback, ct, feedback);
    }

    public async Task<WorkflowExecution> RetryStepAsync(string conversationId, CancellationToken ct = default)
    {
        var execution = await _executionService.GetByConversationAsync(conversationId)
            ?? throw new InvalidOperationException($"No workflow found for conversation {conversationId}");

        if (execution.CurrentState != WorkflowState.Failed)
            throw new InvalidOperationException($"Can only retry from Failed state, current: {execution.CurrentState}");

        execution.RetryCount++;
        RecordDecision(execution, WorkflowDecisionType.RetryRequested, "User", $"Retry #{execution.RetryCount}");

        return await TransitionAndExecuteAsync(execution, WorkflowState.Analyzing, "Retry requested", ct);
    }

    public Task<WorkflowExecution?> GetExecutionAsync(string conversationId)
        => _executionService.GetByConversationAsync(conversationId);

    public async Task CancelWorkflowAsync(string conversationId)
    {
        var execution = await _executionService.GetByConversationAsync(conversationId);
        if (execution == null) return;

        execution.CurrentState = WorkflowState.Failed;
        execution.ErrorMessage = "Cancelled by user";
        RecordDecision(execution, WorkflowDecisionType.UserIntervention, "User", "Workflow cancelled");
        await _executionService.SaveAsync(execution);
    }

    // ── Core execution logic ─────────────────────────────────────────

    private async Task<WorkflowExecution> TransitionAndExecuteAsync(
        WorkflowExecution execution, WorkflowState newState, string reason,
        CancellationToken ct, string? userFeedback = null)
    {
        var oldState = execution.CurrentState;

        if (!_stateMachine.TryTransition(oldState, newState, out var transitionError))
        {
            _logger.LogError("Invalid transition {From} -> {To}: {Reason}", oldState, newState, transitionError);
            throw new InvalidOperationException(transitionError);
        }

        execution.CurrentState = newState;
        RecordDecision(execution, WorkflowDecisionType.StateTransition, "Engine", reason, oldState, newState);
        await _executionService.SaveAsync(execution);

        if (OnStateChanged != null)
            await OnStateChanged.Invoke(execution.ConversationId, newState);

        return await ExecuteCurrentStateAsync(execution, ct, userFeedback);
    }

    private async Task<WorkflowExecution> ExecuteCurrentStateAsync(
        WorkflowExecution execution, CancellationToken ct, string? userFeedback = null)
    {
        var settings = await _settingsService.GetAsync();
        var flow = settings.TeamFlow;

        try
        {
            switch (execution.CurrentState)
            {
                case WorkflowState.Analyzing:
                    await ExecuteAnalysisStepAsync(execution, flow, ct, userFeedback);
                    break;

                case WorkflowState.Planned:
                    if (flow.RequireUserApprovalBeforeCoding)
                    {
                        // Ask user to approve the plan before proceeding
                        var orchAgent = await FindAgentByRoleAsync(AgentRole.Orchestrator);
                        if (orchAgent != null)
                        {
                            execution.PendingQuestions = [WorkflowStrings.PlanApprovalQuestion];
                            await EmitChatAsync(execution.ConversationId, new WorkflowChatMessage
                            {
                                Sender = orchAgent.Name,
                                SenderRole = "Orchestrator",
                                Content = WorkflowStrings.PlanApprovalMessage,
                                MessageType = nameof(MessageType.WorkflowQuestion)
                            });
                        }
                        RecordDecision(execution, WorkflowDecisionType.StepSkipped, "Engine",
                            "Awaiting user approval before coding");
                        execution.CurrentState = WorkflowState.WaitingForInput;
                        await _executionService.SaveAsync(execution);
                    }
                    else
                    {
                        await TransitionAndExecuteAsync(execution, WorkflowState.Coding, "Plan approved, starting implementation", ct);
                    }
                    break;

                case WorkflowState.Coding:
                    await ExecuteAgentStepAsync(execution, AgentRole.Coder, flow, ct);
                    break;

                case WorkflowState.Reviewing:
                    await ExecuteAgentStepAsync(execution, AgentRole.Reviewer, flow, ct);
                    break;

                case WorkflowState.ChangesRequested:
                    execution.ReviewCycleCount++;
                    if (execution.ReviewCycleCount >= flow.MaxReviewCycles)
                    {
                        await TransitionAndExecuteAsync(execution, WorkflowState.Blocked,
                            $"Max review cycles ({flow.MaxReviewCycles}) reached", ct);
                    }
                    else
                    {
                        await TransitionAndExecuteAsync(execution, WorkflowState.Coding,
                            $"Changes requested (cycle {execution.ReviewCycleCount})", ct);
                    }
                    break;

                case WorkflowState.Testing:
                    await ExecuteAgentStepAsync(execution, AgentRole.Tester, flow, ct);
                    break;

                case WorkflowState.ReadyToMerge:
                    await TransitionAndExecuteAsync(execution, WorkflowState.Completed, "All checks passed", ct);
                    break;

                case WorkflowState.Completed:
                    execution.CompletedAt = DateTime.UtcNow;
                    await _executionService.SaveAsync(execution);
                    await EmitChatAsync(execution.ConversationId, new WorkflowChatMessage
                    {
                        Sender = "System",
                        SenderRole = "System",
                        Content = "Workflow voltooid! Alle stappen zijn succesvol afgerond.",
                        MessageType = nameof(MessageType.SystemEvent)
                    });
                    break;

                case WorkflowState.WaitingForInput:
                    // Nothing to do — waiting for user
                    await _executionService.SaveAsync(execution);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Workflow step failed for conversation {ConversationId}", execution.ConversationId);
            execution.CurrentState = WorkflowState.Failed;
            execution.ErrorMessage = ex.Message;
            await _executionService.SaveAsync(execution);

            var orchestrator = await FindAgentByRoleAsync(AgentRole.Orchestrator);
            await EmitChatAsync(execution.ConversationId,
                _composer.ComposeError(orchestrator?.Name ?? "System", "Orchestrator", ex.Message));
        }

        return execution;
    }

    // ── Analysis step ────────────────────────────────────────────────

    private async Task ExecuteAnalysisStepAsync(
        WorkflowExecution execution, TeamFlowConfiguration flow, CancellationToken ct, string? userFeedback = null)
    {
        var orchestrator = await FindAgentByRoleAsync(AgentRole.Orchestrator)
            ?? throw new InvalidOperationException("No orchestrator agent configured");

        var conversation = await _conversationService.GetByIdAsync(execution.ConversationId)
            ?? throw new InvalidOperationException("Conversation not found");

        var input = await BuildTaskInputAsync(execution, orchestrator, "Analyze task and create implementation plan", userFeedback);
        var step = CreateStep(execution, orchestrator, "Analyze task");

        // Save structured input
        var inputJson = JsonSerializer.Serialize(input, JsonOpts);
        step.InputJsonPath = await _executionService.SaveStepInputAsync(execution.ConversationId, step.Id, inputJson);

        // Execute agent
        step.Status = WorkflowStepStatus.Running;
        step.StartedAt = DateTime.UtcNow;
        await _executionService.SaveAsync(execution);

        var run = await _agentRunService.ExecuteRunToCompletionAsync(execution.ConversationId, orchestrator.Id,
            _promptBuilder.BuildTaskPrompt(input, orchestrator), ct, skipChatMessage: true);

        step.AgentRunId = run.Id;
        var rawOutput = run.OutputText ?? "";

        // Save raw output
        step.OutputJsonPath = await _executionService.SaveStepOutputAsync(execution.ConversationId, step.Id, rawOutput);
        step.CompletedAt = DateTime.UtcNow;
        step.DurationMs = run.DurationMs;

        // Parse structured result
        var parseResult = _outputParser.ParseOrchestratorOutput(rawOutput);

        if (parseResult.Success && parseResult.Value != null)
        {
            step.Status = WorkflowStepStatus.Succeeded;
            var analysis = parseResult.Value;

            // If the orchestrator included questions, treat it as AskQuestions
            // regardless of the stated action — getting answers has priority.
            if (analysis.Questions.Count > 0 && analysis.Action != OrchestratorAction.AskQuestions)
            {
                _logger.LogInformation("Overriding orchestrator action {Action} -> AskQuestions because questions were provided",
                    analysis.Action);
                analysis.Action = OrchestratorAction.AskQuestions;
            }

            // Determine next state based on orchestrator action
            switch (analysis.Action)
            {
                case OrchestratorAction.PresentOptions:
                    execution.PendingOptions = analysis.Options.Select(o => new WorkflowOption
                    {
                        Number = o.Number,
                        Title = o.Title,
                        Description = o.Description,
                        Recommendation = o.IsRecommended ? "Recommended" : null
                    }).ToList();
                    await EmitChatAsync(execution.ConversationId, _composer.ComposeOptionsPresentation(orchestrator, analysis));
                    await TransitionAndExecuteAsync(execution, WorkflowState.WaitingForInput, "Presenting options to user", ct);
                    break;

                case OrchestratorAction.AskQuestions:
                    execution.PendingQuestions = analysis.Questions;
                    await EmitChatAsync(execution.ConversationId, _composer.ComposeQuestions(orchestrator, analysis));
                    await TransitionAndExecuteAsync(execution, WorkflowState.WaitingForInput, "Asking user for clarification", ct);
                    break;

                case OrchestratorAction.ProposePlan:
                    execution.PlannedSteps = analysis.PlannedSteps;
                    if (analysis.PlannedSteps.Count == 0)
                        _logger.LogWarning("ProposePlan returned with empty plannedSteps for {ExecutionId}", execution.Id);
                    await EmitChatAsync(execution.ConversationId, _composer.ComposePlanPresentation(orchestrator, analysis));
                    await TransitionAndExecuteAsync(execution, WorkflowState.Planned, "Plan created", ct);
                    break;

                case OrchestratorAction.ProceedDirectly:
                    execution.PlannedSteps = analysis.PlannedSteps;
                    if (analysis.PlannedSteps.Count == 0)
                        _logger.LogWarning("ProceedDirectly returned with empty plannedSteps for {ExecutionId}", execution.Id);
                    await EmitChatAsync(execution.ConversationId, _composer.ComposePlanPresentation(orchestrator, analysis));
                    await TransitionAndExecuteAsync(execution, WorkflowState.Planned, "Proceeding directly", ct);
                    break;

                case OrchestratorAction.ChatResponse:
                    // Casual chat — post message and go back to WaitingForInput (no workflow progression)
                    await EmitChatAsync(execution.ConversationId, new WorkflowChatMessage
                    {
                        Sender = orchestrator.Name,
                        SenderRole = "Orchestrator",
                        Content = analysis.ChatMessage ?? analysis.Summary,
                        MessageType = nameof(MessageType.AgentResponse)
                    });
                    RecordDecision(execution, WorkflowDecisionType.StepSkipped, "Engine",
                        "Chat response — no workflow action needed");
                    await TransitionAndExecuteAsync(execution, WorkflowState.WaitingForInput,
                        "Chat response sent, awaiting further input", ct);
                    break;
            }
        }
        else
        {
            // Parsing failed — use the raw output as the chat message
            step.Status = WorkflowStepStatus.Failed;
            step.ErrorMessage = parseResult.ErrorMessage;
            _logger.LogWarning("Orchestrator output parsing failed: {Error}. Raw output length: {Length}",
                parseResult.ErrorMessage, rawOutput.Length);

            // skipChatMessage=true means the response processor did NOT post to chat.
            // We MUST post the raw output here so the user sees something.
            var fallbackContent = string.IsNullOrWhiteSpace(rawOutput)
                ? "Ik kon de taak niet volledig analyseren. Kan je meer context geven?"
                : CleanRawOutputForChat(rawOutput);

            await EmitChatAsync(execution.ConversationId, new WorkflowChatMessage
            {
                Sender = orchestrator.Name,
                SenderRole = "Orchestrator",
                Content = fallbackContent,
                MessageType = nameof(MessageType.Plan)
            });

            await TransitionAndExecuteAsync(execution, WorkflowState.WaitingForInput, "Parse failed — awaiting user guidance", ct);
        }

        await _executionService.SaveAsync(execution);
    }

    // ── Agent step (Coder, Reviewer, Tester) ─────────────────────────

    private async Task ExecuteAgentStepAsync(
        WorkflowExecution execution, AgentRole role, TeamFlowConfiguration flow, CancellationToken ct)
    {
        var agent = await FindAgentByRoleAsync(role);
        if (agent == null)
        {
            // Optional step — skip if no agent configured
            var flowStep = flow.Steps.FirstOrDefault(s => s.AgentRole == role.ToString());
            if (flowStep?.IsOptional == true)
            {
                RecordDecision(execution, WorkflowDecisionType.StepSkipped, "Engine", $"No {role} agent configured, skipping optional step");
                var skipOutcome = new WorkflowStepOutcome { Success = true, AgentRole = role.ToString() };
                var skipNextState = _stateMachine.DetermineNextState(execution.CurrentState, skipOutcome);
                await TransitionAndExecuteAsync(execution, skipNextState, $"Skipped {role} (no agent configured)", ct);
                return;
            }
            throw new InvalidOperationException($"No {role} agent configured");
        }

        var orchestrator = await FindAgentByRoleAsync(AgentRole.Orchestrator);
        var conversation = await _conversationService.GetByIdAsync(execution.ConversationId);

        // Compose delegation chat message (role-specific defaults are used, no previous step summary leak)
        var delegationFlowStep = flow.Steps.FirstOrDefault(s => s.AgentRole == role.ToString()) ?? new FlowStep { Action = $"Execute {role} work" };

        if (orchestrator != null)
        {
            await EmitChatAsync(execution.ConversationId,
                _composer.ComposeDelegation(orchestrator, agent, delegationFlowStep, conversation?.Title ?? ""));
        }

        // Build input
        var actionDesc = delegationFlowStep.Action;
        var input = await BuildTaskInputAsync(execution, agent, actionDesc);
        var step = CreateStep(execution, agent, actionDesc);

        var inputJson = JsonSerializer.Serialize(input, JsonOpts);
        step.InputJsonPath = await _executionService.SaveStepInputAsync(execution.ConversationId, step.Id, inputJson);

        // Execute
        step.Status = WorkflowStepStatus.Running;
        step.StartedAt = DateTime.UtcNow;
        await _executionService.SaveAsync(execution);

        var run = await _agentRunService.ExecuteRunToCompletionAsync(execution.ConversationId, agent.Id,
            _promptBuilder.BuildTaskPrompt(input, agent), ct, skipChatMessage: true);

        step.AgentRunId = run.Id;
        var rawOutput = run.OutputText ?? "";
        step.OutputJsonPath = await _executionService.SaveStepOutputAsync(execution.ConversationId, step.Id, rawOutput);
        step.CompletedAt = DateTime.UtcNow;
        step.DurationMs = run.DurationMs;

        // Parse and determine next state
        var outcome = new WorkflowStepOutcome { AgentRole = role.ToString() };

        switch (role)
        {
            case AgentRole.Coder:
                var coderResult = _outputParser.ParseCoderOutput(rawOutput);
                if (coderResult.Success && coderResult.Value != null)
                {
                    step.Status = WorkflowStepStatus.Succeeded;
                    step.Summary = coderResult.Value.Summary;
                    outcome.Success = coderResult.Value.CanContinueToReview;
                    await EmitChatAsync(execution.ConversationId,
                        _composer.ComposeStepComplete(agent, coderResult.Value.Summary, rawOutput));
                }
                else
                {
                    step.Status = WorkflowStepStatus.Succeeded; // Agent ran, output just wasn't structured
                    step.Summary = coderResult.FallbackSummary ?? "Implementatie afgerond.";
                    outcome.Success = true;
                    await EmitChatAsync(execution.ConversationId,
                        _composer.ComposeStepComplete(agent, step.Summary, rawOutput));
                }
                break;

            case AgentRole.Reviewer:
                var reviewResult = _outputParser.ParseReviewerOutput(rawOutput);
                if (reviewResult.Success && reviewResult.Value != null)
                {
                    step.Status = WorkflowStepStatus.Succeeded;
                    step.Summary = reviewResult.Value.Summary;
                    outcome.Success = true;
                    outcome.ReviewDecision = reviewResult.Value.Decision;

                    // Track nice-to-have suggestions so the state machine can ask the user
                    if (reviewResult.Value.NiceToHave.Count > 0)
                    {
                        outcome.HasSuggestions = true;
                        outcome.Suggestions = reviewResult.Value.NiceToHave.Select(s => s.Description).ToList();
                    }

                    await EmitChatAsync(execution.ConversationId,
                        _composer.ComposeReviewResult(agent, reviewResult.Value));
                }
                else
                {
                    step.Status = WorkflowStepStatus.Succeeded;
                    step.Summary = reviewResult.FallbackSummary ?? "Review afgerond.";
                    outcome.Success = true;
                    outcome.ReviewDecision = ReviewDecision.ApprovedWithSuggestions; // Optimistic fallback
                    await EmitChatAsync(execution.ConversationId,
                        _composer.ComposeStepComplete(agent, step.Summary, rawOutput));
                }
                break;

            case AgentRole.Tester:
                var testResult = _outputParser.ParseTesterOutput(rawOutput);
                if (testResult.Success && testResult.Value != null)
                {
                    step.Status = WorkflowStepStatus.Succeeded;
                    step.Summary = testResult.Value.Summary;
                    outcome.Success = true;
                    outcome.TestDecision = testResult.Value.Decision;
                    await EmitChatAsync(execution.ConversationId,
                        _composer.ComposeTestResult(agent, testResult.Value));
                }
                else
                {
                    step.Status = WorkflowStepStatus.Succeeded;
                    step.Summary = testResult.FallbackSummary ?? "Tests afgerond.";
                    outcome.Success = true;
                    outcome.TestDecision = TestDecision.AllPassed; // Optimistic fallback
                    await EmitChatAsync(execution.ConversationId,
                        _composer.ComposeStepComplete(agent, step.Summary, rawOutput));
                }
                break;

            default:
                step.Status = WorkflowStepStatus.Succeeded;
                step.Summary = "Stap afgerond.";
                outcome.Success = true;
                await EmitChatAsync(execution.ConversationId,
                    _composer.ComposeStepComplete(agent, step.Summary, rawOutput));
                break;
        }

        await _executionService.SaveAsync(execution);

        // Determine and transition to next state
        var nextState = _stateMachine.DetermineNextState(execution.CurrentState, outcome);

        // When reviewer has suggestions and workflow pauses for user approval, emit the question
        if (nextState == WorkflowState.WaitingForInput && outcome.HasSuggestions && outcome.Suggestions.Count > 0)
        {
            var reviewerAgent = agent;
            var coderAgent = await FindAgentByRoleAsync(AgentRole.Coder);
            var orchAgent = await FindAgentByRoleAsync(AgentRole.Orchestrator);
            if (orchAgent != null && coderAgent != null)
            {
                execution.PendingQuestions = outcome.Suggestions;
                await EmitChatAsync(execution.ConversationId,
                    _composer.ComposeSuggestionsApproval(orchAgent, reviewerAgent, coderAgent, outcome.Suggestions));
            }
        }

        await TransitionAndExecuteAsync(execution, nextState,
            $"{role} completed: {outcome.ReviewDecision?.ToString() ?? outcome.TestDecision?.ToString() ?? "OK"}", ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private async Task<AgentTaskInput> BuildTaskInputAsync(
        WorkflowExecution execution, AgentDefinition agent, string action, string? userFeedback = null)
    {
        var conversation = await _conversationService.GetByIdAsync(execution.ConversationId);
        var messages = await _messageService.GetByConversationAsync(execution.ConversationId);
        var artifacts = await _artifactService.GetByConversationAsync(execution.ConversationId);

        // Gather team members so the agent knows the full team
        var allAgents = (await _agentService.GetAllAsync()).Where(a => a.IsEnabled).ToList();
        var teamMembers = allAgents
            .Where(a => a.Id != agent.Id) // exclude the current agent
            .Select(a => new Core.Contracts.TeamMemberInfo
            {
                Name = a.Name,
                Role = a.Role.ToString(),
                Description = a.Description ?? ""
            }).ToList();

        // Build previous step summaries
        var previousSteps = execution.Steps
            .Where(s => s.Status == WorkflowStepStatus.Succeeded)
            .Select(s => new StepSummary
            {
                AgentName = s.AgentName,
                Role = s.AgentRole,
                Action = s.Action,
                Summary = s.Action // Will be enriched from output JSON if available
            }).ToList();

        // Build artifact references
        var artifactRefs = artifacts.Select(a => new ArtifactReference
        {
            ArtifactId = a.Id,
            FileName = a.FileName,
            Type = a.Type.ToString(),
            CreatedBy = a.CreatedByAgent
        }).ToList();

        // Use only the original task description (first user message) as context.
        // Follow-up messages are passed via UserFeedback to avoid duplication.
        var contextSummary = messages
            .Where(m => m.SenderRole == "User")
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Content)
            .FirstOrDefault() ?? "";

        return new AgentTaskInput
        {
            TaskId = execution.Id,
            ConversationId = execution.ConversationId,
            StepId = execution.Steps.LastOrDefault()?.Id ?? "",
            Title = conversation?.Title ?? "Unknown task",
            Goal = conversation?.Description ?? "",
            ContextSummary = contextSummary,
            AssignedRole = agent.Role.ToString(),
            AssignedAgentName = agent.Name,
            Action = action,
            PreviousSteps = previousSteps,
            RelevantArtifacts = artifactRefs,
            TeamMembers = teamMembers,
            UserFeedback = userFeedback,
            ExpectedOutputFormat = _promptBuilder.BuildOutputFormatInstructions(agent.Role)
        };
    }

    private WorkflowStep CreateStep(WorkflowExecution execution, AgentDefinition agent, string action)
    {
        var step = new WorkflowStep
        {
            StepIndex = execution.Steps.Count,
            AgentRole = agent.Role.ToString(),
            AgentName = agent.Name,
            AgentId = agent.Id,
            Action = action
        };
        execution.Steps.Add(step);
        return step;
    }

    private async Task<AgentDefinition?> FindAgentByRoleAsync(AgentRole role)
    {
        var agents = await _agentService.GetAllAsync();
        return agents.FirstOrDefault(a => a.Role == role && a.IsEnabled);
    }

    private async Task EmitChatAsync(string conversationId, WorkflowChatMessage chatMsg)
    {
        // Persist as a real message
        var msgType = Enum.TryParse<MessageType>(chatMsg.MessageType, out var mt) ? mt : MessageType.SystemEvent;
        var message = await _messageService.AddAsync(conversationId, chatMsg.Sender, chatMsg.SenderRole,
            msgType, chatMsg.Content);

        // Create context block if detailed context is provided
        if (!string.IsNullOrWhiteSpace(chatMsg.DetailedContext))
        {
            await _contextBlockService.CreateAsync(conversationId, message.Id,
                chatMsg.DetailedContextLabel ?? "Details", chatMsg.DetailedContext, chatMsg.Sender);
        }

        // Notify UI
        if (OnChatMessage != null)
            await OnChatMessage.Invoke(conversationId, chatMsg);
    }

    private static void RecordDecision(WorkflowExecution execution, WorkflowDecisionType type, string maker, string reason,
        WorkflowState? from = null, WorkflowState? to = null)
    {
        execution.Decisions.Add(new WorkflowDecision
        {
            Type = type,
            DecisionMaker = maker,
            Reason = reason,
            FromState = from ?? execution.CurrentState,
            ToState = to
        });
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "...";

    /// <summary>
    /// Strips JSON blocks and CLI artifacts from raw agent output, leaving only
    /// human-readable prose suitable for the chat window.
    /// </summary>
    private static string CleanRawOutputForChat(string raw)
    {
        var lines = raw.Split('\n');
        var cleaned = new List<string>();
        var inCodeBlock = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```"))
            {
                inCodeBlock = !inCodeBlock;
                continue; // skip fence lines
            }

            if (inCodeBlock) continue; // skip code block content (JSON etc.)

            // Skip CLI tool-use lines
            if (trimmed.StartsWith("✓ ") || trimmed.StartsWith("$ ") || trimmed.StartsWith("↪ "))
                continue;

            // Strip leading ● bullet
            if (trimmed.StartsWith("● "))
            {
                cleaned.Add(line.Replace("● ", ""));
                continue;
            }

            cleaned.Add(line);
        }

        var result = string.Join("\n", cleaned).Trim();
        return string.IsNullOrWhiteSpace(result) ? raw.Trim() : result;
    }
}
