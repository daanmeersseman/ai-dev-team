using AiDevTeam.Core.Contracts;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.Services.Workflow;

namespace AiDevTeam.Tests.Workflow;

public class PromptBuilderTests
{
    private readonly PromptBuilder _builder = new();

    private static AgentDefinition CreateAgent(AgentRole role = AgentRole.Coder) => new()
    {
        Name = "Sam (Coder)",
        Role = role,
        Description = "Implements features and writes clean code.",
        SystemPrompt = "You are Sam, a senior developer.",
        Personality = "Pragmatic and efficient",
        Backstory = "8 years of full-stack experience.",
        Expertise = new() { "C#", ".NET", "TypeScript" },
        Values = new() { "Clean code", "Pragmatic solutions" },
        CommunicationQuirks = new() { "Uses code snippets", "Casual tone" },
        CommunicationStyle = "casual"
    };

    // ── System prompt ────────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_includes_agent_identity()
    {
        var agent = CreateAgent();
        var prompt = _builder.BuildSystemPrompt(agent);

        Assert.Contains("Sam", prompt);
        Assert.Contains("real team member", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_includes_personality()
    {
        var agent = CreateAgent();
        var prompt = _builder.BuildSystemPrompt(agent);

        Assert.Contains("Pragmatic and efficient", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_includes_backstory()
    {
        var agent = CreateAgent();
        var prompt = _builder.BuildSystemPrompt(agent);

        Assert.Contains("8 years", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_includes_expertise()
    {
        var agent = CreateAgent();
        var prompt = _builder.BuildSystemPrompt(agent);

        Assert.Contains("C#", prompt);
        Assert.Contains(".NET", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_includes_values()
    {
        var agent = CreateAgent();
        var prompt = _builder.BuildSystemPrompt(agent);

        Assert.Contains("Clean code", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_includes_communication_quirks()
    {
        var agent = CreateAgent();
        var prompt = _builder.BuildSystemPrompt(agent);

        Assert.Contains("Casual tone", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_includes_custom_system_prompt()
    {
        var agent = CreateAgent();
        var prompt = _builder.BuildSystemPrompt(agent);

        Assert.Contains("You are Sam", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_without_optional_fields_still_works()
    {
        var agent = new AgentDefinition { Name = "Test", Role = AgentRole.Coder };
        var prompt = _builder.BuildSystemPrompt(agent);

        Assert.Contains("Test", prompt);
        Assert.Contains("Coder", prompt);
    }

    // ── Task prompt ──────────────────────────────────────────────────

    [Fact]
    public void BuildTaskPrompt_includes_task_info()
    {
        var input = new AgentTaskInput
        {
            Title = "Add user authentication",
            Goal = "Implement JWT-based auth",
            Action = "Implement the auth controller"
        };

        var prompt = _builder.BuildTaskPrompt(input, CreateAgent());

        Assert.Contains("Add user authentication", prompt);
        Assert.Contains("JWT-based auth", prompt);
        Assert.Contains("Implement the auth controller", prompt);
    }

    [Fact]
    public void BuildTaskPrompt_includes_constraints()
    {
        var input = new AgentTaskInput
        {
            Title = "Test",
            Goal = "Test",
            Constraints = new() { "Use .NET 8", "No third-party packages" }
        };

        var prompt = _builder.BuildTaskPrompt(input, CreateAgent());

        Assert.Contains("Use .NET 8", prompt);
        Assert.Contains("No third-party packages", prompt);
    }

    [Fact]
    public void BuildTaskPrompt_includes_previous_steps()
    {
        var input = new AgentTaskInput
        {
            Title = "Test",
            Goal = "Test",
            PreviousSteps = new()
            {
                new StepSummary { AgentName = "Alex", Role = "Orchestrator", Action = "Created plan", Summary = "Plan includes 3 phases" }
            }
        };

        var prompt = _builder.BuildTaskPrompt(input, CreateAgent());

        Assert.Contains("Alex", prompt);
        Assert.Contains("Plan includes 3 phases", prompt);
    }

    [Fact]
    public void BuildTaskPrompt_includes_artifacts()
    {
        var input = new AgentTaskInput
        {
            Title = "Test",
            Goal = "Test",
            RelevantArtifacts = new()
            {
                new ArtifactReference { FileName = "plan.md", Type = "Markdown", CreatedBy = "Alex" }
            }
        };

        var prompt = _builder.BuildTaskPrompt(input, CreateAgent());

        Assert.Contains("plan.md", prompt);
    }

    [Fact]
    public void BuildTaskPrompt_includes_user_feedback()
    {
        var input = new AgentTaskInput
        {
            Title = "Test",
            Goal = "Test",
            UserFeedback = "Use option 1 with the existing controller"
        };

        var prompt = _builder.BuildTaskPrompt(input, CreateAgent());

        Assert.Contains("Use option 1", prompt);
    }

    // ── Output format instructions ───────────────────────────────────

    [Fact]
    public void BuildOutputFormatInstructions_orchestrator_includes_json_schema()
    {
        var format = _builder.BuildOutputFormatInstructions(AgentRole.Orchestrator);

        Assert.Contains("json", format);
        Assert.Contains("action", format);
        Assert.Contains("ProposePlan", format);
    }

    [Fact]
    public void BuildOutputFormatInstructions_orchestrator_reinforces_no_tools()
    {
        var format = _builder.BuildOutputFormatInstructions(AgentRole.Orchestrator);

        Assert.Contains("Do NOT use tools", format);
    }

    [Fact]
    public void BuildTaskPrompt_orchestrator_includes_reminder_section()
    {
        var agent = CreateAgent(AgentRole.Orchestrator);
        var input = new AgentTaskInput
        {
            Title = "Test",
            Goal = "Test",
            ExpectedOutputFormat = "```json\n{}\n```"
        };

        var prompt = _builder.BuildTaskPrompt(input, agent);

        Assert.Contains("## REMINDER", prompt);
        Assert.Contains("Do NOT use tools", prompt);
    }

    [Fact]
    public void BuildOutputFormatInstructions_coder_includes_changed_files()
    {
        var format = _builder.BuildOutputFormatInstructions(AgentRole.Coder);

        Assert.Contains("changedFiles", format);
        Assert.Contains("canContinueToReview", format);
    }

    [Fact]
    public void BuildOutputFormatInstructions_reviewer_includes_decision_field()
    {
        var format = _builder.BuildOutputFormatInstructions(AgentRole.Reviewer);

        Assert.Contains("decision", format);
        Assert.Contains("mustFix", format);
    }

    [Fact]
    public void BuildOutputFormatInstructions_tester_includes_test_counts()
    {
        var format = _builder.BuildOutputFormatInstructions(AgentRole.Tester);

        Assert.Contains("testsPassed", format);
        Assert.Contains("testsFailed", format);
    }
}
