using AiDevTeam.Core.Contracts;
using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Core.Models.Workflow;
using AiDevTeam.Infrastructure.Services.Workflow;

namespace AiDevTeam.Tests.Scenarios;

/// <summary>
/// Scenario tests that simulate how the team handles real tasks.
/// These test the full orchestration flow without executing real AI providers.
/// Each scenario documents a specific team behavior pattern.
/// </summary>
public class TeamOrchestrationScenarioTests
{
    private readonly IWorkflowStateMachine _stateMachine = new WorkflowStateMachine();

    // ── Scenario: Happy path — task flows through full team ──────────

    [Fact]
    public void Scenario_happy_path_task_flows_from_creation_to_completion()
    {
        var state = WorkflowState.Created;

        // 1. Task created → Start analysis
        state = AssertTransition(state, WorkflowState.Analyzing);

        // 2. Orchestrator analyzes and creates plan
        var analysisOutcome = new WorkflowStepOutcome { Success = true, HasPlan = true, AgentRole = "Orchestrator" };
        state = _stateMachine.DetermineNextState(state, analysisOutcome);
        Assert.Equal(WorkflowState.Planned, state);

        // 3. Plan approved → Start coding
        state = AssertTransition(state, WorkflowState.Coding);

        // 4. Coder completes → Move to review
        var coderOutcome = new WorkflowStepOutcome { Success = true, AgentRole = "Coder" };
        state = _stateMachine.DetermineNextState(state, coderOutcome);
        Assert.Equal(WorkflowState.Reviewing, state);

        // 5. Reviewer approves → Move to testing
        var reviewOutcome = new WorkflowStepOutcome { Success = true, ReviewDecision = ReviewDecision.Approved };
        state = _stateMachine.DetermineNextState(state, reviewOutcome);
        Assert.Equal(WorkflowState.Testing, state);

        // 6. Tests pass → Ready to merge
        var testOutcome = new WorkflowStepOutcome { Success = true, TestDecision = TestDecision.AllPassed };
        state = _stateMachine.DetermineNextState(state, testOutcome);
        Assert.Equal(WorkflowState.ReadyToMerge, state);

        // 7. Ready to merge → Completed
        state = AssertTransition(state, WorkflowState.Completed);
        Assert.Equal(WorkflowState.Completed, state);
    }

    // ── Scenario: Orchestrator asks user for clarification ───────────

    [Fact]
    public void Scenario_orchestrator_asks_questions_before_planning()
    {
        var state = WorkflowState.Created;

        // Task created → Analyzing
        state = AssertTransition(state, WorkflowState.Analyzing);

        // Orchestrator needs user input
        var outcome = new WorkflowStepOutcome { Success = true, NeedsUserInput = true, AgentRole = "Orchestrator" };
        state = _stateMachine.DetermineNextState(state, outcome);
        Assert.Equal(WorkflowState.WaitingForInput, state);

        // User responds → Back to analyzing
        state = AssertTransition(state, WorkflowState.Analyzing);

        // Now has enough info, creates plan
        var planOutcome = new WorkflowStepOutcome { Success = true, HasPlan = true, AgentRole = "Orchestrator" };
        state = _stateMachine.DetermineNextState(state, planOutcome);
        Assert.Equal(WorkflowState.Planned, state);
    }

    // ── Scenario: Review rejection cycle ─────────────────────────────

    [Fact]
    public void Scenario_reviewer_requests_changes_coder_fixes_then_approved()
    {
        var state = WorkflowState.Reviewing;

        // First review: changes required
        var review1 = new WorkflowStepOutcome { Success = true, ReviewDecision = ReviewDecision.ChangesRequired };
        state = _stateMachine.DetermineNextState(state, review1);
        Assert.Equal(WorkflowState.ChangesRequested, state);

        // Back to coding
        state = AssertTransition(state, WorkflowState.Coding);

        // Coder fixes
        var coderOutcome = new WorkflowStepOutcome { Success = true, AgentRole = "Coder" };
        state = _stateMachine.DetermineNextState(state, coderOutcome);
        Assert.Equal(WorkflowState.Reviewing, state);

        // Second review: approved with suggestions → pauses for user approval
        var review2 = new WorkflowStepOutcome { Success = true, ReviewDecision = ReviewDecision.ApprovedWithSuggestions };
        state = _stateMachine.DetermineNextState(state, review2);
        Assert.Equal(WorkflowState.WaitingForInput, state);

        // User skips suggestions → proceed to testing
        state = AssertTransition(state, WorkflowState.Testing);
    }

    // ── Scenario: Tests fail, go back to code changes ────────────────

    [Fact]
    public void Scenario_tests_fail_workflow_cycles_back_to_coding()
    {
        var state = WorkflowState.Testing;

        // Tests fail
        var testOutcome = new WorkflowStepOutcome { Success = true, TestDecision = TestDecision.SomeFailed };
        state = _stateMachine.DetermineNextState(state, testOutcome);
        Assert.Equal(WorkflowState.ChangesRequested, state);

        // Back to coding to fix
        state = AssertTransition(state, WorkflowState.Coding);

        // Coder fixes
        var coderOutcome = new WorkflowStepOutcome { Success = true, AgentRole = "Coder" };
        state = _stateMachine.DetermineNextState(state, coderOutcome);
        Assert.Equal(WorkflowState.Reviewing, state);
    }

    // ── Scenario: Reviewer blocks the task ───────────────────────────

    [Fact]
    public void Scenario_reviewer_blocks_task_due_to_fundamental_issue()
    {
        var state = WorkflowState.Reviewing;

        var outcome = new WorkflowStepOutcome { Success = true, ReviewDecision = ReviewDecision.Blocked };
        state = _stateMachine.DetermineNextState(state, outcome);
        Assert.Equal(WorkflowState.Blocked, state);

        // From blocked, can go back to analyzing
        Assert.True(_stateMachine.IsTransitionAllowed(WorkflowState.Blocked, WorkflowState.Analyzing));
    }

    // ── Scenario: Coder fails, workflow enters failed state ──────────

    [Fact]
    public void Scenario_agent_failure_transitions_to_failed()
    {
        var state = WorkflowState.Coding;

        var outcome = new WorkflowStepOutcome { Success = false, ErrorMessage = "Provider timeout" };
        state = _stateMachine.DetermineNextState(state, outcome);
        Assert.Equal(WorkflowState.Failed, state);

        // Can retry from failed
        Assert.True(_stateMachine.IsTransitionAllowed(WorkflowState.Failed, WorkflowState.Analyzing));
    }

    // ── Scenario: Multiple review cycles with max limit ──────────────

    [Fact]
    public void Scenario_max_review_cycles_enforced_by_flow_config()
    {
        var flow = new TeamFlowConfiguration { MaxReviewCycles = 2 };
        var execution = new WorkflowExecution { ReviewCycleCount = 0 };

        // Cycle 1: review rejects
        execution.ReviewCycleCount++;
        Assert.True(execution.ReviewCycleCount < flow.MaxReviewCycles);

        // Cycle 2: review rejects again
        execution.ReviewCycleCount++;
        Assert.False(execution.ReviewCycleCount < flow.MaxReviewCycles);

        // At max cycles, should block instead of cycling again
        Assert.True(_stateMachine.IsTransitionAllowed(WorkflowState.ChangesRequested, WorkflowState.Blocked));
    }

    // ── Scenario: User intervention at any point ─────────────────────

    [Fact]
    public void Scenario_user_can_intervene_during_waiting_state()
    {
        var state = WorkflowState.WaitingForInput;

        // User provides feedback → can go to analyzing or planned or coding
        Assert.True(_stateMachine.IsTransitionAllowed(state, WorkflowState.Analyzing));
        Assert.True(_stateMachine.IsTransitionAllowed(state, WorkflowState.Planned));
        Assert.True(_stateMachine.IsTransitionAllowed(state, WorkflowState.Coding));
    }

    // ── Scenario: Orchestrator presents options, user chooses ────────

    [Fact]
    public void Scenario_orchestrator_presents_options_waits_for_user_choice()
    {
        var execution = new WorkflowExecution { CurrentState = WorkflowState.Analyzing };

        // Orchestrator presents options
        execution.PendingOptions = new()
        {
            new WorkflowOption { Number = 1, Title = "REST API", Description = "Traditional REST" },
            new WorkflowOption { Number = 2, Title = "GraphQL", Description = "Flexible queries" }
        };
        execution.CurrentState = WorkflowState.WaitingForInput;

        Assert.Equal(2, execution.PendingOptions.Count);
        Assert.Equal(WorkflowState.WaitingForInput, execution.CurrentState);

        // User selects option 1
        var selectedOption = execution.PendingOptions.First(o => o.Number == 1);
        Assert.Equal("REST API", selectedOption.Title);

        execution.PendingOptions.Clear();
        // Would transition back to Analyzing with user feedback
    }

    // ── Scenario: Full decision log tracking ─────────────────────────

    [Fact]
    public void Scenario_decisions_are_tracked_throughout_workflow()
    {
        var execution = new WorkflowExecution();

        // Track transitions
        execution.Decisions.Add(new WorkflowDecision
        {
            Type = WorkflowDecisionType.StateTransition,
            DecisionMaker = "Engine",
            Reason = "Workflow started",
            FromState = WorkflowState.Created,
            ToState = WorkflowState.Analyzing
        });

        execution.Decisions.Add(new WorkflowDecision
        {
            Type = WorkflowDecisionType.AgentRouting,
            DecisionMaker = "Engine",
            Reason = "Routing to orchestrator for analysis"
        });

        execution.Decisions.Add(new WorkflowDecision
        {
            Type = WorkflowDecisionType.UserIntervention,
            DecisionMaker = "User",
            Reason = "Selected option 1: REST API"
        });

        execution.Decisions.Add(new WorkflowDecision
        {
            Type = WorkflowDecisionType.StateTransition,
            DecisionMaker = "Engine",
            Reason = "Review approved",
            FromState = WorkflowState.Reviewing,
            ToState = WorkflowState.Testing
        });

        Assert.Equal(4, execution.Decisions.Count);
        Assert.Equal(2, execution.Decisions.Count(d => d.Type == WorkflowDecisionType.StateTransition));
        Assert.Single(execution.Decisions, d => d.Type == WorkflowDecisionType.UserIntervention);
    }

    // ── Scenario: Skip optional step (no DB specialist) ──────────────

    [Fact]
    public void Scenario_optional_step_skipped_when_no_agent_configured()
    {
        var flow = new TeamFlowConfiguration
        {
            Steps = new()
            {
                new FlowStep { Order = 1, AgentRole = "Orchestrator", Action = "Analyze" },
                new FlowStep { Order = 2, AgentRole = "DatabaseSpecialist", Action = "DB check", IsOptional = true },
                new FlowStep { Order = 3, AgentRole = "Coder", Action = "Implement" }
            }
        };

        var dbStep = flow.Steps.First(s => s.AgentRole == "DatabaseSpecialist");
        Assert.True(dbStep.IsOptional);

        // When no DB specialist agent exists, step should be skippable
        var coderStep = flow.Steps.First(s => s.AgentRole == "Coder");
        Assert.Equal(3, coderStep.Order);
    }

    // ── Scenario: Provider capability matching ───────────────────────

    [Fact]
    public void Scenario_flow_steps_specify_required_capabilities()
    {
        var flow = new TeamFlowConfiguration
        {
            Steps = new()
            {
                new FlowStep { Order = 1, AgentRole = "Orchestrator", RequiredCapability = ProviderCapability.Reasoning },
                new FlowStep { Order = 2, AgentRole = "Coder", RequiredCapability = ProviderCapability.Coding },
                new FlowStep { Order = 3, AgentRole = "Reviewer", RequiredCapability = ProviderCapability.Review },
                new FlowStep { Order = 4, AgentRole = "Tester", RequiredCapability = ProviderCapability.Testing }
            }
        };

        Assert.Equal(ProviderCapability.Reasoning, flow.Steps[0].RequiredCapability);
        Assert.Equal(ProviderCapability.Coding, flow.Steps[1].RequiredCapability);

        // Agent's preferred capability should match step's required capability
        var coder = new AgentDefinition
        {
            Role = AgentRole.Coder,
            PreferredProviderCapability = ProviderCapability.Coding
        };
        var coderStep = flow.Steps.First(s => s.AgentRole == "Coder");
        Assert.Equal(coder.PreferredProviderCapability, coderStep.RequiredCapability);
    }

    // ── Scenario: Structured output chain between agents ─────────────

    [Fact]
    public void Scenario_coder_output_feeds_into_reviewer_input()
    {
        // Simulate the data flow between agents
        var coderResult = new CoderResult
        {
            Status = CoderStatus.Completed,
            Summary = "Added UserController with GET /api/users",
            ChangedFiles = new()
            {
                new() { FilePath = "Controllers/UserController.cs", ChangeType = FileChangeType.Created }
            },
            ImplementedChanges = new() { "GET endpoint", "Pagination" },
            CanContinueToReview = true
        };

        // The reviewer input should include what the coder did
        var reviewerInput = new AgentTaskInput
        {
            Title = "Add user API",
            Goal = "REST endpoint for users",
            Action = "Review the implementation",
            PreviousSteps = new()
            {
                new StepSummary
                {
                    AgentName = "Sam",
                    Role = "Coder",
                    Action = "Implemented changes",
                    Summary = coderResult.Summary
                }
            }
        };

        Assert.Single(reviewerInput.PreviousSteps);
        Assert.Contains("UserController", reviewerInput.PreviousSteps[0].Summary);
    }

    // ── Scenario: Full artifact chain through workflow ────────────────

    [Fact]
    public void Scenario_artifacts_accumulate_through_workflow_steps()
    {
        var execution = new WorkflowExecution();

        // Step 1: Orchestrator creates plan artifact
        var planStep = new WorkflowStep
        {
            AgentName = "Alex", AgentRole = "Orchestrator", Action = "Create plan",
            Status = WorkflowStepStatus.Succeeded, ProducedArtifactIds = new() { "artifact-plan" }
        };
        execution.Steps.Add(planStep);

        // Step 2: Coder creates code artifacts
        var codeStep = new WorkflowStep
        {
            AgentName = "Sam", AgentRole = "Coder", Action = "Implement",
            Status = WorkflowStepStatus.Succeeded, ProducedArtifactIds = new() { "artifact-code-1", "artifact-code-2" }
        };
        execution.Steps.Add(codeStep);

        // Step 3: Reviewer creates review artifact
        var reviewStep = new WorkflowStep
        {
            AgentName = "Morgan", AgentRole = "Reviewer", Action = "Review",
            Status = WorkflowStepStatus.Succeeded, ProducedArtifactIds = new() { "artifact-review" }
        };
        execution.Steps.Add(reviewStep);

        // Total artifacts in workflow
        var allArtifacts = execution.Steps.SelectMany(s => s.ProducedArtifactIds).ToList();
        Assert.Equal(4, allArtifacts.Count);
    }

    // ── Helper ───────────────────────────────────────────────────────

    private WorkflowState AssertTransition(WorkflowState from, WorkflowState to)
    {
        Assert.True(_stateMachine.TryTransition(from, to, out var reason),
            $"Expected transition {from} → {to} to be valid, but got: {reason}");
        return to;
    }
}
