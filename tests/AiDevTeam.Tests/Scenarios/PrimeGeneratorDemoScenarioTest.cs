using AiDevTeam.Core.Contracts;
using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Core.Models.Workflow;
using AiDevTeam.Infrastructure.Services.Workflow;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AiDevTeam.Tests.Scenarios;

/// <summary>
/// End-to-end scenario test simulating the "version 7.0" conversation:
/// User asks the team to demo building a C# prime generator function.
/// Verifies the full workflow from analysis through coding, review, testing to completion,
/// with human-readable chat messages and proper team collaboration at every step.
/// </summary>
public class PrimeGeneratorDemoScenarioTest
{
    private const string ConversationId = "conv-prime-demo";
    private const string TeamId = "team-1";

    // Real components (business logic under test)
    private readonly WorkflowStateMachine _stateMachine = new();
    private readonly OutputParser _outputParser;
    private readonly ChatMessageComposer _composer = new();
    private readonly PromptBuilder _promptBuilder = new();

    // Mocked external dependencies
    private readonly IWorkflowExecutionService _executionService = Substitute.For<IWorkflowExecutionService>();
    private readonly IConversationService _conversationService = Substitute.For<IConversationService>();
    private readonly IMessageService _messageService = Substitute.For<IMessageService>();
    private readonly IArtifactService _artifactService = Substitute.For<IArtifactService>();
    private readonly IAgentDefinitionService _agentService = Substitute.For<IAgentDefinitionService>();
    private readonly IAppSettingsService _settingsService = Substitute.For<IAppSettingsService>();
    private readonly IAgentRunService _agentRunService = Substitute.For<IAgentRunService>();
    private readonly IContextBlockService _contextBlockService = Substitute.For<IContextBlockService>();
    private readonly IProviderConfigurationService _providerService = Substitute.For<IProviderConfigurationService>();

    // Captured chat messages for assertions
    private readonly List<WorkflowChatMessage> _chatMessages = new();

    // Agent definitions
    private static readonly AgentDefinition Orchestrator = new()
    {
        Id = "agent-alex", Name = "Alex (Tech Lead)", Role = AgentRole.Orchestrator,
        Description = "Orchestrates tasks, creates plans, coordinates team.",
        Color = "#F9E2AF", IsEnabled = true
    };
    private static readonly AgentDefinition Coder = new()
    {
        Id = "agent-sam", Name = "Sam (Coder)", Role = AgentRole.Coder,
        Description = "Implements features, writes clean code.",
        Color = "#A6E3A1", IsEnabled = true
    };
    private static readonly AgentDefinition Reviewer = new()
    {
        Id = "agent-morgan", Name = "Morgan (Reviewer)", Role = AgentRole.Reviewer,
        Description = "Reviews code for quality and best practices.",
        Color = "#CBA6F7", IsEnabled = true
    };
    private static readonly AgentDefinition Tester = new()
    {
        Id = "agent-riley", Name = "Riley (Tester)", Role = AgentRole.Tester,
        Description = "Writes tests, validates quality.",
        Color = "#F38BA8", IsEnabled = true
    };

    private readonly WorkflowEngine _engine;

    public PrimeGeneratorDemoScenarioTest()
    {
        _outputParser = new OutputParser(Substitute.For<ILogger<OutputParser>>());

        var allAgents = new List<AgentDefinition> { Orchestrator, Coder, Reviewer, Tester };

        // Setup agent service
        _agentService.GetAllAsync().Returns(allAgents);
        _agentService.GetByIdAsync(Orchestrator.Id).Returns(Orchestrator);
        _agentService.GetByIdAsync(Coder.Id).Returns(Coder);
        _agentService.GetByIdAsync(Reviewer.Id).Returns(Reviewer);
        _agentService.GetByIdAsync(Tester.Id).Returns(Tester);

        // Setup conversation
        var conversation = new Conversation
        {
            Id = ConversationId, Title = "Prime Generator Demo", TeamId = TeamId, Status = ConversationStatus.InProgress
        };
        _conversationService.GetByIdAsync(ConversationId).Returns(conversation);
        _conversationService.UpdateAsync(Arg.Any<Conversation>()).Returns(ci => ci.Arg<Conversation>());

        // Setup settings with flow that requires user approval
        var settings = new AppSettings
        {
            TeamFlow = new TeamFlowConfiguration
            {
                Name = "Default Development Flow",
                RequireUserApprovalBeforeCoding = false, // auto-proceed for demo
                Steps = new()
                {
                    new FlowStep { Order = 1, AgentRole = "Orchestrator", Action = "Analyze task" },
                    new FlowStep { Order = 2, AgentRole = "Coder", Action = "Implement changes" },
                    new FlowStep { Order = 3, AgentRole = "Reviewer", Action = "Review implementation" },
                    new FlowStep { Order = 4, AgentRole = "Tester", Action = "Run tests", IsOptional = true }
                }
            }
        };
        _settingsService.GetAsync().Returns(settings);

        // Setup message service
        _messageService.GetByConversationAsync(ConversationId).Returns(new List<Message>
        {
            new() { ConversationId = ConversationId, Sender = "You", SenderRole = "User",
                Content = "Hi team! I want to give a demo to my \"human\" team. Show me how you build a function in c# to generate prime numbers. Don't create a project or implement anything. Just show how you work together as a team via artifacts. I want a final answer from my tech lead about how the function looks like in the chat.",
                Type = MessageType.UserInstruction }
        });
#pragma warning disable CS0618
        _messageService.AddAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<MessageType>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(ci => new Message
            {
                ConversationId = ci.ArgAt<string>(0), Sender = ci.ArgAt<string>(1),
                SenderRole = ci.ArgAt<string>(2), Content = ci.ArgAt<string>(4)
            });
#pragma warning restore CS0618

        // Setup artifact service
        _artifactService.GetByConversationAsync(ConversationId).Returns(new List<Artifact>());
        _artifactService.GetArtifactDirectory(ConversationId).Returns("/tmp/artifacts");

        // Setup context block service
        _contextBlockService.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => new ContextBlock { Id = Guid.NewGuid().ToString() });

        // Setup execution service — store in memory
        WorkflowExecution? storedExecution = null;
        _executionService.SaveAsync(Arg.Any<WorkflowExecution>())
            .Returns(ci => { storedExecution = ci.Arg<WorkflowExecution>(); return Task.CompletedTask; });
        _executionService.GetByConversationAsync(ConversationId)
            .Returns(ci => Task.FromResult(storedExecution));
        _executionService.SaveStepInputAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns("step-input.json");
        _executionService.SaveStepOutputAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns("step-output.json");

        // Create engine
        _engine = new WorkflowEngine(
            _stateMachine, _executionService, _promptBuilder, _outputParser,
            _conversationService, _messageService, _artifactService, _agentService,
            _settingsService, _agentRunService, _contextBlockService, _providerService,
            _composer, Substitute.For<ILogger<WorkflowEngine>>());

        // Capture chat messages
        _engine.OnChatMessage += (convId, msg) =>
        {
            _chatMessages.Add(msg);
            return Task.CompletedTask;
        };
    }

    // ── Simulated agent outputs ──────────────────────────────────────

    /// <summary>
    /// Orchestrator output with CLI ● prefix (like real v7.0 output).
    /// Uses ProposePlan with plannedSteps so the workflow can delegate.
    /// </summary>
    private const string OrchestratorOutput = """
        ● ```json
           {
             "action": "ProposePlan",
             "summary": "Prime number generation function - team demo with collaborative workflow",
             "chatMessage": "Hey team! We've got a fun one — building a C# prime number generator to demo our workflow.\n\nHere's the plan:\n1. **Sam** will implement a clean `GeneratePrimes` function using the Sieve of Eratosthenes\n2. **Morgan** will review the code for quality and performance\n3. **Riley** will write unit tests covering edge cases\n\nLet's show how a real team ships quality code!",
             "plan": "## Prime Generator Demo Plan\n\n1. Sam implements `PrimeGenerator.GeneratePrimesUpTo(int limit)` using Sieve of Eratosthenes\n2. Morgan reviews algorithm correctness, naming, edge cases\n3. Riley validates with unit tests: 0, 1, 2, negative, large numbers",
             "plannedSteps": [
               { "order": 1, "agentRole": "Coder", "description": "Implement PrimeGenerator class with GeneratePrimesUpTo method using Sieve of Eratosthenes", "isOptional": false },
               { "order": 2, "agentRole": "Reviewer", "description": "Review implementation for correctness, performance, and code quality", "isOptional": false },
               { "order": 3, "agentRole": "Tester", "description": "Write unit tests covering edge cases and validate correctness", "isOptional": false }
             ],
             "risks": ["Algorithm choice may be overkill for small ranges"],
             "estimatedComplexity": "Low"
           }
           ```
        """;

    private const string CoderOutput = """
        ● ```json
           {
             "status": "Completed",
             "summary": "Implemented PrimeGenerator class with two methods: GeneratePrimesUpTo (Sieve of Eratosthenes) and IsPrime (trial division). Clean, well-documented code ready for review.",
             "changedFiles": [
               { "filePath": "PrimeGenerator.cs", "changeType": "Created", "description": "Core prime generation class with Sieve of Eratosthenes and trial division" }
             ],
             "implementedChanges": [
               "GeneratePrimesUpTo(int limit) using Sieve of Eratosthenes — O(n log log n)",
               "IsPrime(int number) using optimized trial division — O(sqrt(n))",
               "Input validation for edge cases (negative, 0, 1)"
             ],
             "canContinueToReview": true
           }
           ```
        """;

    private const string ReviewerOutput = """
        ● ```json
           {
             "decision": "Approved",
             "summary": "Clean implementation. Sieve of Eratosthenes is the right choice. Good edge case handling. Code is readable and well-structured.",
             "mustFix": [],
             "niceToHave": [
               { "description": "Consider adding XML doc comments for public API", "severity": "Info" },
               { "description": "Could add an overload that returns IEnumerable<int> for lazy evaluation", "severity": "Info" }
             ],
             "testGaps": ["Test with limit = int.MaxValue boundary"],
             "securityConcerns": [],
             "blockers": []
           }
           ```
        """;

    private const string TesterOutput = """
        ● ```json
           {
             "decision": "AllPassed",
             "summary": "All 8 unit tests passed. Edge cases covered: negative input, zero, one, small primes, known prime sequences, performance within acceptable range.",
             "testsPassed": 8,
             "testsFailed": 0,
             "testsSkipped": 0,
             "failures": [],
             "testGaps": [],
             "notes": ["Performance test: 168 primes up to 1000 generated in <1ms"]
           }
           ```
        """;

    // ── The test ─────────────────────────────────────────────────────

    [Fact]
    public async Task Scenario_prime_generator_demo_full_team_collaboration()
    {
        // Setup: mock agent run responses in sequence
        var callIndex = 0;
        _agentRunService.ExecuteRunToCompletionAsync(
            ConversationId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), skipChatMessage: true)
            .Returns(ci =>
            {
                var agentId = ci.ArgAt<string>(1);
                var output = (callIndex++) switch
                {
                    0 => OrchestratorOutput,  // Step 1: Alex analyzes
                    1 => CoderOutput,          // Step 2: Sam codes
                    2 => ReviewerOutput,       // Step 3: Morgan reviews
                    3 => TesterOutput,         // Step 4: Riley tests
                    _ => throw new InvalidOperationException($"Unexpected call #{callIndex}")
                };
                return Task.FromResult(new AgentRun
                {
                    Id = $"run-{callIndex}", AgentDefinitionId = agentId,
                    ConversationId = ConversationId, Status = AgentRunStatus.Succeeded,
                    OutputText = output, DurationMs = 5000
                });
            });

        // ── ACT: Start the workflow ──────────────────────────────────
        var execution = await _engine.StartWorkflowAsync(ConversationId);

        // ── ASSERT: Workflow pauses for suggestions approval ─────────
        // Morgan approved with niceToHave suggestions → engine asks user for approval
        Assert.Equal(WorkflowState.WaitingForInput, execution.CurrentState);
        Assert.Equal(3, callIndex); // Orchestrator + Coder + Reviewer called so far

        // ── ASSERT: Suggestions approval message was posted ──────────
        var suggestionsMsg = _chatMessages.FirstOrDefault(m =>
            m.Sender == "Alex (Tech Lead)" && m.MessageType == nameof(MessageType.WorkflowQuestion)
            && m.Content.Contains("suggestions", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(suggestionsMsg);
        Assert.Contains("XML doc comments", suggestionsMsg!.Content);
        Assert.Contains("Sam", suggestionsMsg.Content);

        // ── ACT: User says "no" to skip suggestions ─────────────────
        execution = await _engine.ContinueWorkflowAsync(ConversationId, "No, skip and continue to testing");

        // ── ASSERT: Workflow completed successfully ───────────────────
        Assert.Equal(WorkflowState.Completed, execution.CurrentState);

        // ── ASSERT: All 4 agents were called ─────────────────────────
        Assert.Equal(4, callIndex);

        // ── ASSERT: Chat messages are human-readable ─────────────────
        // Expected: Alex plan, delegation→Sam, Sam result, delegation→Morgan,
        // Morgan review, suggestions question, delegation→Riley, Riley result, completion
        Assert.True(_chatMessages.Count >= 9,
            $"Expected at least 9 chat messages, got {_chatMessages.Count}:\n" +
            string.Join("\n", _chatMessages.Select((m, i) => $"  [{i}] {m.Sender}: {Truncate(m.Content, 80)}")));

        // ── ASSERT: Alex's plan is the first message and reads naturally ──
        var alexPlan = _chatMessages.First(m => m.Sender == "Alex (Tech Lead)");
        Assert.Contains("Sam", alexPlan.Content);
        Assert.Contains("Morgan", alexPlan.Content);
        Assert.Contains("Riley", alexPlan.Content);
        // Should NOT contain raw JSON
        Assert.DoesNotContain("\"action\"", alexPlan.Content);
        Assert.DoesNotContain("\"summary\"", alexPlan.Content);

        // ── ASSERT: Delegation messages exist ────────────────────────
        var delegations = _chatMessages.Where(m => m.MessageType == nameof(MessageType.WorkflowDelegation)).ToList();
        Assert.True(delegations.Count >= 3,
            $"Expected at least 3 delegation messages, got {delegations.Count}");

        // Sam was delegated to
        Assert.Contains(delegations, m => m.Content.Contains("Sam"));
        // Morgan was delegated to
        Assert.Contains(delegations, m => m.Content.Contains("Morgan"));
        // Riley was delegated to
        Assert.Contains(delegations, m => m.Content.Contains("Riley"));

        // ── ASSERT: Step completion messages are human-readable ──────
        var samComplete = _chatMessages.First(m => m.Sender == "Sam (Coder)" && m.MessageType == nameof(MessageType.WorkflowStepComplete));
        Assert.Contains("PrimeGenerator", samComplete.Content);
        Assert.DoesNotContain("```json", samComplete.Content);

        var morganComplete = _chatMessages.First(m => m.Sender == "Morgan (Reviewer)");
        Assert.Contains("Approved", morganComplete.Content);

        var rileyComplete = _chatMessages.First(m => m.Sender == "Riley (Tester)");
        Assert.Contains("8", rileyComplete.Content); // 8 tests passed

        // ── ASSERT: Completion message exists ────────────────────────
        var completionMsg = _chatMessages.Last();
        Assert.Equal("System", completionMsg.Sender);
        Assert.Contains("voltooid", completionMsg.Content);

        // ── ASSERT: Workflow steps recorded correctly ─────────────────
        // orchestrator + coder + reviewer + tester = 4 agent steps minimum
        var succeededSteps = execution.Steps.Where(s => s.Status == WorkflowStepStatus.Succeeded).ToList();
        Assert.True(succeededSteps.Count >= 4,
            $"Expected at least 4 succeeded steps, got {succeededSteps.Count}: " +
            string.Join(", ", execution.Steps.Select(s => $"{s.AgentRole}={s.Status}")));

        // ── ASSERT: Decisions are tracked ────────────────────────────
        // More decisions now due to suggestions approval flow
        Assert.True(execution.Decisions.Count >= 7,
            $"Expected at least 7 decisions, got {execution.Decisions.Count}");

        // ── DIAGNOSTIC: Uncomment to inspect the full chat flow ──────
        // var chatDump = string.Join("\n\n", _chatMessages.Select((m, i) =>
        //     $"--- Message {i + 1}: {m.Sender} ({m.SenderRole}) [{m.MessageType}] ---\n{m.Content}"));
        // Assert.Fail($"Chat flow dump:\n\n{chatDump}");
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text.Replace("\n", " ") : text[..max].Replace("\n", " ") + "...";
}
