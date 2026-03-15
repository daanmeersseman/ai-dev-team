using AiDevTeam.Core.Contracts;
using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models.Workflow;
using AiDevTeam.Infrastructure.Services.Workflow;

namespace AiDevTeam.Tests.Workflow;

public class WorkflowStateMachineTests
{
    private readonly IWorkflowStateMachine _sm = new WorkflowStateMachine();

    // ── Valid transitions ────────────────────────────────────────────

    [Fact]
    public void Created_can_transition_to_Analyzing()
    {
        Assert.True(_sm.IsTransitionAllowed(WorkflowState.Created, WorkflowState.Analyzing));
    }

    [Fact]
    public void Analyzing_can_transition_to_WaitingForInput()
    {
        Assert.True(_sm.IsTransitionAllowed(WorkflowState.Analyzing, WorkflowState.WaitingForInput));
    }

    [Fact]
    public void Analyzing_can_transition_to_Planned()
    {
        Assert.True(_sm.IsTransitionAllowed(WorkflowState.Analyzing, WorkflowState.Planned));
    }

    [Fact]
    public void Planned_can_transition_to_Coding()
    {
        Assert.True(_sm.IsTransitionAllowed(WorkflowState.Planned, WorkflowState.Coding));
    }

    [Fact]
    public void Coding_can_transition_to_Reviewing()
    {
        Assert.True(_sm.IsTransitionAllowed(WorkflowState.Coding, WorkflowState.Reviewing));
    }

    [Fact]
    public void Reviewing_can_transition_to_ChangesRequested()
    {
        Assert.True(_sm.IsTransitionAllowed(WorkflowState.Reviewing, WorkflowState.ChangesRequested));
    }

    [Fact]
    public void Reviewing_can_transition_to_Testing()
    {
        Assert.True(_sm.IsTransitionAllowed(WorkflowState.Reviewing, WorkflowState.Testing));
    }

    [Fact]
    public void Reviewing_can_transition_to_ReadyToMerge()
    {
        Assert.True(_sm.IsTransitionAllowed(WorkflowState.Reviewing, WorkflowState.ReadyToMerge));
    }

    [Fact]
    public void Testing_can_transition_to_ReadyToMerge()
    {
        Assert.True(_sm.IsTransitionAllowed(WorkflowState.Testing, WorkflowState.ReadyToMerge));
    }

    [Fact]
    public void ReadyToMerge_can_transition_to_Completed()
    {
        Assert.True(_sm.IsTransitionAllowed(WorkflowState.ReadyToMerge, WorkflowState.Completed));
    }

    [Fact]
    public void ChangesRequested_can_transition_back_to_Coding()
    {
        Assert.True(_sm.IsTransitionAllowed(WorkflowState.ChangesRequested, WorkflowState.Coding));
    }

    // ── Invalid transitions ──────────────────────────────────────────

    [Fact]
    public void Created_cannot_skip_to_Coding()
    {
        Assert.False(_sm.IsTransitionAllowed(WorkflowState.Created, WorkflowState.Coding));
    }

    [Fact]
    public void Completed_cannot_transition_anywhere()
    {
        Assert.Empty(_sm.GetAllowedTransitions(WorkflowState.Completed));
    }

    [Fact]
    public void Coding_cannot_skip_to_Completed()
    {
        Assert.False(_sm.IsTransitionAllowed(WorkflowState.Coding, WorkflowState.Completed));
    }

    [Fact]
    public void Reviewing_cannot_go_back_to_Analyzing()
    {
        Assert.False(_sm.IsTransitionAllowed(WorkflowState.Reviewing, WorkflowState.Analyzing));
    }

    // ── TryTransition ────────────────────────────────────────────────

    [Fact]
    public void TryTransition_returns_true_for_valid_transition()
    {
        var result = _sm.TryTransition(WorkflowState.Created, WorkflowState.Analyzing, out var reason);
        Assert.True(result);
        Assert.Null(reason);
    }

    [Fact]
    public void TryTransition_returns_false_with_reason_for_invalid_transition()
    {
        var result = _sm.TryTransition(WorkflowState.Created, WorkflowState.Completed, out var reason);
        Assert.False(result);
        Assert.NotNull(reason);
        Assert.Contains("not allowed", reason);
    }

    // ── DetermineNextState ───────────────────────────────────────────

    [Fact]
    public void DetermineNextState_Analyzing_with_user_input_needed_goes_to_WaitingForInput()
    {
        var outcome = new WorkflowStepOutcome { Success = true, NeedsUserInput = true };
        Assert.Equal(WorkflowState.WaitingForInput, _sm.DetermineNextState(WorkflowState.Analyzing, outcome));
    }

    [Fact]
    public void DetermineNextState_Analyzing_with_plan_goes_to_Planned()
    {
        var outcome = new WorkflowStepOutcome { Success = true, HasPlan = true };
        Assert.Equal(WorkflowState.Planned, _sm.DetermineNextState(WorkflowState.Analyzing, outcome));
    }

    [Fact]
    public void DetermineNextState_Reviewing_approved_goes_to_Testing()
    {
        var outcome = new WorkflowStepOutcome { Success = true, ReviewDecision = ReviewDecision.Approved };
        Assert.Equal(WorkflowState.Testing, _sm.DetermineNextState(WorkflowState.Reviewing, outcome));
    }

    [Fact]
    public void DetermineNextState_Reviewing_changes_required_goes_to_ChangesRequested()
    {
        var outcome = new WorkflowStepOutcome { Success = true, ReviewDecision = ReviewDecision.ChangesRequired };
        Assert.Equal(WorkflowState.ChangesRequested, _sm.DetermineNextState(WorkflowState.Reviewing, outcome));
    }

    [Fact]
    public void DetermineNextState_Reviewing_approved_with_suggestions_goes_to_Testing()
    {
        // Minor suggestions don't block the pipeline — continue to testing
        var outcome = new WorkflowStepOutcome { Success = true, ReviewDecision = ReviewDecision.ApprovedWithSuggestions };
        Assert.Equal(WorkflowState.Testing, _sm.DetermineNextState(WorkflowState.Reviewing, outcome));
    }

    [Fact]
    public void DetermineNextState_Reviewing_approved_with_HasSuggestions_goes_to_Testing()
    {
        // Approved + suggestions should also continue to testing
        var outcome = new WorkflowStepOutcome { Success = true, ReviewDecision = ReviewDecision.Approved, HasSuggestions = true };
        Assert.Equal(WorkflowState.Testing, _sm.DetermineNextState(WorkflowState.Reviewing, outcome));
    }

    [Fact]
    public void DetermineNextState_Reviewing_blocked_goes_to_Blocked()
    {
        var outcome = new WorkflowStepOutcome { Success = true, ReviewDecision = ReviewDecision.Blocked };
        Assert.Equal(WorkflowState.Blocked, _sm.DetermineNextState(WorkflowState.Reviewing, outcome));
    }

    [Fact]
    public void DetermineNextState_Testing_all_passed_goes_to_ReadyToMerge()
    {
        var outcome = new WorkflowStepOutcome { Success = true, TestDecision = TestDecision.AllPassed };
        Assert.Equal(WorkflowState.ReadyToMerge, _sm.DetermineNextState(WorkflowState.Testing, outcome));
    }

    [Fact]
    public void DetermineNextState_Testing_some_failed_goes_to_ChangesRequested()
    {
        var outcome = new WorkflowStepOutcome { Success = true, TestDecision = TestDecision.SomeFailed };
        Assert.Equal(WorkflowState.ChangesRequested, _sm.DetermineNextState(WorkflowState.Testing, outcome));
    }

    [Fact]
    public void DetermineNextState_failure_always_goes_to_Failed()
    {
        var outcome = new WorkflowStepOutcome { Success = false };
        Assert.Equal(WorkflowState.Failed, _sm.DetermineNextState(WorkflowState.Coding, outcome));
        Assert.Equal(WorkflowState.Failed, _sm.DetermineNextState(WorkflowState.Reviewing, outcome));
        Assert.Equal(WorkflowState.Failed, _sm.DetermineNextState(WorkflowState.Testing, outcome));
    }

    // ── Every state can reach Failed ─────────────────────────────────

    [Theory]
    [InlineData(WorkflowState.Analyzing)]
    [InlineData(WorkflowState.WaitingForInput)]
    [InlineData(WorkflowState.Planned)]
    [InlineData(WorkflowState.Coding)]
    [InlineData(WorkflowState.Reviewing)]
    [InlineData(WorkflowState.ChangesRequested)]
    [InlineData(WorkflowState.Testing)]
    [InlineData(WorkflowState.ReadyToMerge)]
    [InlineData(WorkflowState.Blocked)]
    public void Every_active_state_can_transition_to_Failed(WorkflowState state)
    {
        Assert.True(_sm.IsTransitionAllowed(state, WorkflowState.Failed));
    }

    // ── Failed can retry via Analyzing ───────────────────────────────

    [Fact]
    public void Failed_can_transition_to_Analyzing_for_retry()
    {
        Assert.True(_sm.IsTransitionAllowed(WorkflowState.Failed, WorkflowState.Analyzing));
    }

    // ── GetAllowedTransitions ────────────────────────────────────────

    [Fact]
    public void GetAllowedTransitions_returns_all_valid_targets()
    {
        var transitions = _sm.GetAllowedTransitions(WorkflowState.Reviewing);
        Assert.Contains(WorkflowState.ChangesRequested, transitions);
        Assert.Contains(WorkflowState.Testing, transitions);
        Assert.Contains(WorkflowState.WaitingForInput, transitions);
        Assert.Contains(WorkflowState.ReadyToMerge, transitions);
        Assert.Contains(WorkflowState.Blocked, transitions);
        Assert.Contains(WorkflowState.Failed, transitions);
    }
}
