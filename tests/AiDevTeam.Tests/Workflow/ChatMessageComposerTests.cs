using AiDevTeam.Core.Contracts;
using AiDevTeam.Core.Models;
using AiDevTeam.Core.Models.Workflow;
using AiDevTeam.Infrastructure.Services.Workflow;

namespace AiDevTeam.Tests.Workflow;

public class ChatMessageComposerTests
{
    private readonly ChatMessageComposer _composer = new();

    private static AgentDefinition Orchestrator() => new()
    {
        Name = "Alex (Tech Lead)", Role = AgentRole.Orchestrator, Color = "#F9E2AF"
    };

    private static AgentDefinition Coder() => new()
    {
        Name = "Sam (Coder)", Role = AgentRole.Coder, Color = "#A6E3A1"
    };

    private static AgentDefinition Reviewer() => new()
    {
        Name = "Morgan (Reviewer)", Role = AgentRole.Reviewer, Color = "#CBA6F7"
    };

    private static AgentDefinition Tester() => new()
    {
        Name = "Riley (Tester)", Role = AgentRole.Tester, Color = "#F38BA8"
    };

    // ── Delegation messages ──────────────────────────────────────────

    [Fact]
    public void ComposeDelegation_uses_first_name_of_target()
    {
        var step = new FlowStep { AgentRole = "Coder", Action = "Implement changes" };
        var msg = _composer.ComposeDelegation(Orchestrator(), Coder(), step, "Add auth");

        Assert.Contains("Sam", msg.Content);
        Assert.Equal("Alex (Tech Lead)", msg.Sender);
    }

    [Fact]
    public void ComposeDelegation_uses_chat_template_when_provided()
    {
        var step = new FlowStep
        {
            AgentRole = "Coder",
            Action = "Implement",
            ChatTemplate = "{agent}, werk aan '{taskTitle}' alsjeblieft."
        };
        var msg = _composer.ComposeDelegation(Orchestrator(), Coder(), step, "User API");

        Assert.Contains("Sam", msg.Content);
        Assert.Contains("User API", msg.Content);
    }

    [Fact]
    public void ComposeDelegation_reviewer_mentions_review()
    {
        var step = new FlowStep { AgentRole = "Reviewer", Action = "Review code" };
        var msg = _composer.ComposeDelegation(Orchestrator(), Reviewer(), step, "");

        Assert.Contains("Morgan", msg.Content);
        Assert.Contains("review", msg.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComposeDelegation_tester_mentions_tests()
    {
        var step = new FlowStep { AgentRole = "Tester", Action = "Run tests" };
        var msg = _composer.ComposeDelegation(Orchestrator(), Tester(), step, "");

        Assert.Contains("Riley", msg.Content);
        Assert.Contains("test", msg.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComposeDelegation_coder_without_summary_does_not_leak_step_action()
    {
        var step = new FlowStep { AgentRole = "Coder", Action = "Implement changes" };
        var msg = _composer.ComposeDelegation(Orchestrator(), Coder(), step, "Prime Generator");

        // Should NOT contain the raw flow step action "Implement changes"
        Assert.DoesNotContain("Implement changes", msg.Content);
        Assert.Contains("Sam", msg.Content);
        Assert.Contains("implementation", msg.Content);
    }

    [Fact]
    public void ComposeDelegation_coder_with_explicit_summary_uses_summary()
    {
        var step = new FlowStep { AgentRole = "Coder", Action = "Implement changes" };
        var msg = _composer.ComposeDelegation(Orchestrator(), Coder(), step, "Task", "Build the REST API");

        Assert.Contains("Build the REST API", msg.Content);
    }

    [Fact]
    public void ComposeDelegation_db_specialist_without_summary_does_not_leak_step_action()
    {
        var dbAgent = new AgentDefinition
        {
            Name = "Jordan (DB Specialist)", Role = AgentRole.DatabaseSpecialist, Color = "#89B4FA"
        };
        var step = new FlowStep { AgentRole = "DatabaseSpecialist", Action = "Review database impact" };
        var msg = _composer.ComposeDelegation(Orchestrator(), dbAgent, step, "User Migration");

        // Should NOT contain the raw flow step action
        Assert.DoesNotContain("Review database impact", msg.Content);
        Assert.Contains("Jordan", msg.Content);
    }

    // ── Options presentation ─────────────────────────────────────────

    [Fact]
    public void ComposeOptionsPresentation_lists_all_options()
    {
        var analysis = new OrchestratorResult
        {
            Summary = "Ik zie twee mogelijkheden",
            Options = new()
            {
                new() { Number = 1, Title = "Option A", Description = "First approach", IsRecommended = true },
                new() { Number = 2, Title = "Option B", Description = "Second approach" }
            }
        };

        var msg = _composer.ComposeOptionsPresentation(Orchestrator(), analysis);

        Assert.Contains("Option A", msg.Content);
        Assert.Contains("Option B", msg.Content);
        Assert.Contains("recommended", msg.Content);
        Assert.Contains("prefer", msg.Content);
    }

    // ── Questions ────────────────────────────────────────────────────

    [Fact]
    public void ComposeQuestions_lists_numbered_questions()
    {
        var analysis = new OrchestratorResult
        {
            Summary = "Ik heb meer info nodig",
            Questions = new() { "Welke database?", "REST of GraphQL?" }
        };

        var msg = _composer.ComposeQuestions(Orchestrator(), analysis);

        Assert.Contains("1. Welke database?", msg.Content);
        Assert.Contains("2. REST of GraphQL?", msg.Content);
    }

    // ── Review result ────────────────────────────────────────────────

    [Fact]
    public void ComposeReviewResult_approved_shows_verdict()
    {
        var result = new ReviewResult
        {
            Decision = ReviewDecision.Approved,
            Summary = "Looks great!"
        };

        var msg = _composer.ComposeReviewResult(Reviewer(), result);

        Assert.Contains("Approved", msg.Content);
        Assert.Equal("Morgan (Reviewer)", msg.Sender);
    }

    [Fact]
    public void ComposeReviewResult_changes_required_shows_must_fix()
    {
        var result = new ReviewResult
        {
            Decision = ReviewDecision.ChangesRequired,
            Summary = "Needs work",
            MustFix = new()
            {
                new() { Description = "Missing null check", FilePath = "UserService.cs", Severity = ReviewSeverity.Error }
            }
        };

        var msg = _composer.ComposeReviewResult(Reviewer(), result);

        Assert.Contains("Missing null check", msg.Content);
        Assert.Contains("UserService.cs", msg.Content);
        Assert.Contains("Changes required", msg.Content);
    }

    // ── Test result ──────────────────────────────────────────────────

    [Fact]
    public void ComposeTestResult_shows_pass_count()
    {
        var result = new TesterResult
        {
            Decision = TestDecision.AllPassed,
            Summary = "All tests passed",
            TestsPassed = 10,
            TestsFailed = 0
        };

        var msg = _composer.ComposeTestResult(Tester(), result);

        Assert.Contains("10/10", msg.Content);
        Assert.Equal("Riley (Tester)", msg.Sender);
    }

    [Fact]
    public void ComposeTestResult_shows_failures()
    {
        var result = new TesterResult
        {
            Decision = TestDecision.SomeFailed,
            Summary = "2 failures",
            TestsPassed = 3,
            TestsFailed = 2,
            Failures = new()
            {
                new() { TestName = "LoginTest", Reason = "Timeout" }
            }
        };

        var msg = _composer.ComposeTestResult(Tester(), result);

        Assert.Contains("LoginTest", msg.Content);
        Assert.Contains("Timeout", msg.Content);
    }

    // ── Suggestions approval ─────────────────────────────────────────

    [Fact]
    public void ComposeSuggestionsApproval_includes_suggestions_and_names()
    {
        var suggestions = new List<string>
        {
            "Add XML doc comments",
            "Consider lazy evaluation with IEnumerable"
        };

        var msg = _composer.ComposeSuggestionsApproval(Orchestrator(), Reviewer(), Coder(), suggestions);

        Assert.Contains("Morgan", msg.Content);
        Assert.Contains("Sam", msg.Content);
        Assert.Contains("Add XML doc comments", msg.Content);
        Assert.Contains("IEnumerable", msg.Content);
        Assert.Contains("yes", msg.Content);
        Assert.Contains("no", msg.Content);
        Assert.Equal("Alex (Tech Lead)", msg.Sender);
        Assert.Equal(nameof(MessageType.WorkflowQuestion), msg.MessageType);
    }

    // ── Plan presentation ────────────────────────────────────────────

    [Fact]
    public void ComposePlanPresentation_does_not_include_json_in_content()
    {
        var analysis = new OrchestratorResult
        {
            ChatMessage = "I've created a plan for the authentication feature.",
            Plan = "{\"phases\":[{\"name\":\"Setup\"}]}",
            Risks = new() { "Token expiry" },
            EstimatedComplexity = "Medium"
        };

        var msg = _composer.ComposePlanPresentation(Orchestrator(), analysis);

        Assert.Equal("I've created a plan for the authentication feature.", msg.Content);
        Assert.DoesNotContain("{", msg.Content);
        Assert.DoesNotContain("Token expiry", msg.Content);
        Assert.DoesNotContain("Medium", msg.Content);
    }

    [Fact]
    public void ComposePlanPresentation_uses_summary_when_no_chatMessage()
    {
        var analysis = new OrchestratorResult
        {
            Summary = "Plan for auth module",
            Plan = "Step 1: create controller"
        };

        var msg = _composer.ComposePlanPresentation(Orchestrator(), analysis);

        Assert.Equal("Plan for auth module", msg.Content);
    }

    [Fact]
    public void ComposePlanPresentation_stores_plan_in_detailed_context()
    {
        var analysis = new OrchestratorResult
        {
            ChatMessage = "Here's the plan.",
            Plan = "{\"step\":\"implement\"}"
        };

        var msg = _composer.ComposePlanPresentation(Orchestrator(), analysis);

        Assert.Equal("{\"step\":\"implement\"}", msg.DetailedContext);
        Assert.Equal(nameof(MessageType.Plan), msg.MessageType);
    }

    // ── Error ────────────────────────────────────────────────────────

    [Fact]
    public void ComposeError_includes_error_message()
    {
        var msg = _composer.ComposeError("Alex (Tech Lead)", "Orchestrator", "Provider timeout");

        Assert.Contains("Provider timeout", msg.Content);
        Assert.Contains("error occurred", msg.Content);
    }
}
