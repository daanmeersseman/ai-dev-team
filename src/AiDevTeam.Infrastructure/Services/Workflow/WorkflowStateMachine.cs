using AiDevTeam.Core.Contracts;
using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Core.Models.Workflow;

namespace AiDevTeam.Infrastructure.Services.Workflow;

public class WorkflowStateMachine : IWorkflowStateMachine
{
    private static readonly Dictionary<WorkflowState, HashSet<WorkflowState>> AllowedTransitions = new()
    {
        [WorkflowState.Created] = new() { WorkflowState.Analyzing },
        [WorkflowState.Analyzing] = new() { WorkflowState.WaitingForInput, WorkflowState.Planned, WorkflowState.Failed, WorkflowState.Blocked },
        [WorkflowState.WaitingForInput] = new() { WorkflowState.Analyzing, WorkflowState.Planned, WorkflowState.Coding, WorkflowState.Testing, WorkflowState.Failed },
        [WorkflowState.Planned] = new() { WorkflowState.Coding, WorkflowState.WaitingForInput, WorkflowState.Failed },
        [WorkflowState.Coding] = new() { WorkflowState.Reviewing, WorkflowState.Blocked, WorkflowState.Failed },
        [WorkflowState.Reviewing] = new() { WorkflowState.ChangesRequested, WorkflowState.Testing, WorkflowState.WaitingForInput, WorkflowState.ReadyToMerge, WorkflowState.Blocked, WorkflowState.Failed },
        [WorkflowState.ChangesRequested] = new() { WorkflowState.Coding, WorkflowState.Blocked, WorkflowState.Failed },
        [WorkflowState.Testing] = new() { WorkflowState.ReadyToMerge, WorkflowState.ChangesRequested, WorkflowState.Blocked, WorkflowState.Failed },
        [WorkflowState.ReadyToMerge] = new() { WorkflowState.Completed, WorkflowState.Failed },
        [WorkflowState.Blocked] = new() { WorkflowState.Analyzing, WorkflowState.Coding, WorkflowState.Reviewing, WorkflowState.Failed },
        [WorkflowState.Failed] = new() { WorkflowState.Analyzing },
        [WorkflowState.Completed] = new HashSet<WorkflowState>()
    };

    public bool TryTransition(WorkflowState from, WorkflowState to, out string? reason)
    {
        if (IsTransitionAllowed(from, to))
        {
            reason = null;
            return true;
        }

        reason = $"Transition from {from} to {to} is not allowed. Valid transitions: {string.Join(", ", GetAllowedTransitions(from))}";
        return false;
    }

    public IReadOnlyList<WorkflowState> GetAllowedTransitions(WorkflowState from)
    {
        return AllowedTransitions.TryGetValue(from, out var transitions)
            ? transitions.ToList().AsReadOnly()
            : Array.Empty<WorkflowState>().AsReadOnly();
    }

    public bool IsTransitionAllowed(WorkflowState from, WorkflowState to)
    {
        return AllowedTransitions.TryGetValue(from, out var transitions) && transitions.Contains(to);
    }

    public WorkflowState DetermineNextState(WorkflowState current, WorkflowStepOutcome outcome)
    {
        if (!outcome.Success)
            return WorkflowState.Failed;

        return current switch
        {
            WorkflowState.Created => WorkflowState.Analyzing,

            WorkflowState.Analyzing when outcome.NeedsUserInput => WorkflowState.WaitingForInput,
            WorkflowState.Analyzing when outcome.HasPlan => WorkflowState.Planned,
            WorkflowState.Analyzing => WorkflowState.Planned,

            WorkflowState.WaitingForInput when outcome.HasPlan => WorkflowState.Planned,
            WorkflowState.WaitingForInput => WorkflowState.Analyzing,

            WorkflowState.Planned => WorkflowState.Coding,

            WorkflowState.Coding when outcome.AgentRole == nameof(AgentRole.Coder) => WorkflowState.Reviewing,

            WorkflowState.Reviewing => outcome.ReviewDecision switch
            {
                ReviewDecision.Approved when outcome.HasSuggestions => WorkflowState.WaitingForInput,
                ReviewDecision.Approved => WorkflowState.Testing,
                ReviewDecision.ApprovedWithSuggestions => WorkflowState.WaitingForInput,
                ReviewDecision.ChangesRequired => WorkflowState.ChangesRequested,
                ReviewDecision.Blocked => WorkflowState.Blocked,
                _ => WorkflowState.Testing
            },

            WorkflowState.ChangesRequested => WorkflowState.Coding,

            WorkflowState.Testing => outcome.TestDecision switch
            {
                TestDecision.AllPassed => WorkflowState.ReadyToMerge,
                TestDecision.SomeFailed => WorkflowState.ChangesRequested,
                TestDecision.Blocked => WorkflowState.Blocked,
                _ => WorkflowState.ReadyToMerge
            },

            WorkflowState.ReadyToMerge => WorkflowState.Completed,

            _ => WorkflowState.Failed
        };
    }
}
