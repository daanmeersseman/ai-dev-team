using AiDevTeam.Core.Contracts;
using AiDevTeam.Core.Models.Workflow;
using AiDevTeam.Infrastructure.Services.Workflow;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiDevTeam.Tests.Workflow;

public class OutputParserTests
{
    private readonly OutputParser _parser = new(NullLogger<OutputParser>.Instance);

    // ── Orchestrator parsing ─────────────────────────────────────────

    [Fact]
    public void ParseOrchestratorOutput_valid_json_in_code_fence()
    {
        var raw = """
            Here is my analysis:

            ```json
            {
              "action": "ProposePlan",
              "summary": "We need to add a REST endpoint",
              "plan": "Step 1: Create controller\nStep 2: Add service",
              "risks": ["Breaking change to API"],
              "estimatedComplexity": "Medium"
            }
            ```
            """;

        var result = _parser.ParseOrchestratorOutput(raw);

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal(OrchestratorAction.ProposePlan, result.Value!.Action);
        Assert.Equal("We need to add a REST endpoint", result.Value.Summary);
        Assert.Single(result.Value.Risks);
    }

    [Fact]
    public void ParseOrchestratorOutput_with_options()
    {
        var raw = """
            ```json
            {
              "action": "PresentOptions",
              "summary": "I see two approaches",
              "options": [
                { "number": 1, "title": "Option A", "description": "First approach", "isRecommended": true },
                { "number": 2, "title": "Option B", "description": "Second approach", "isRecommended": false }
              ]
            }
            ```
            """;

        var result = _parser.ParseOrchestratorOutput(raw);

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.Options.Count);
        Assert.True(result.Value.Options[0].IsRecommended);
    }

    [Fact]
    public void ParseOrchestratorOutput_with_questions()
    {
        var raw = """
            ```json
            {
              "action": "AskQuestions",
              "summary": "I need clarification",
              "questions": ["What database do you use?", "Should we add caching?"]
            }
            ```
            """;

        var result = _parser.ParseOrchestratorOutput(raw);

        Assert.True(result.Success);
        Assert.Equal(2, result.Value!.Questions.Count);
    }

    [Fact]
    public void ParseOrchestratorOutput_freeform_text_returns_fallback()
    {
        var raw = "I think we should start by creating a controller and adding the service layer.";

        var result = _parser.ParseOrchestratorOutput(raw);

        Assert.False(result.Success);
        Assert.NotNull(result.FallbackSummary);
        Assert.Contains("controller", result.FallbackSummary!);
    }

    [Fact]
    public void ParseOrchestratorOutput_empty_input_fails()
    {
        var result = _parser.ParseOrchestratorOutput("");
        Assert.False(result.Success);
        Assert.Contains("Empty", result.ErrorMessage!);
    }

    // ── Coder parsing ────────────────────────────────────────────────

    [Fact]
    public void ParseCoderOutput_valid_result()
    {
        var raw = """
            ```json
            {
              "status": "Completed",
              "summary": "Added UserController with GET endpoint",
              "changedFiles": [
                { "filePath": "Controllers/UserController.cs", "changeType": "Created", "description": "New REST controller" }
              ],
              "implementedChanges": ["GET /api/users endpoint", "Pagination support"],
              "canContinueToReview": true
            }
            ```
            """;

        var result = _parser.ParseCoderOutput(raw);

        Assert.True(result.Success);
        Assert.Equal(CoderStatus.Completed, result.Value!.Status);
        Assert.Single(result.Value.ChangedFiles);
        Assert.True(result.Value.CanContinueToReview);
    }

    [Fact]
    public void ParseCoderOutput_partially_completed()
    {
        var raw = """
            ```json
            {
              "status": "PartiallyCompleted",
              "summary": "Created the controller but couldn't add the service",
              "unresolvedItems": ["Service layer needs DI registration"],
              "canContinueToReview": false
            }
            ```
            """;

        var result = _parser.ParseCoderOutput(raw);

        Assert.True(result.Success);
        Assert.Equal(CoderStatus.PartiallyCompleted, result.Value!.Status);
        Assert.False(result.Value.CanContinueToReview);
    }

    // ── Reviewer parsing ─────────────────────────────────────────────

    [Fact]
    public void ParseReviewerOutput_approved()
    {
        var raw = """
            ```json
            {
              "decision": "Approved",
              "summary": "Clean implementation, looks good",
              "niceToHave": [
                { "description": "Could add XML docs", "severity": "Info" }
              ]
            }
            ```
            """;

        var result = _parser.ParseReviewerOutput(raw);

        Assert.True(result.Success);
        Assert.Equal(ReviewDecision.Approved, result.Value!.Decision);
        Assert.True(result.Value.CanMerge);
    }

    [Fact]
    public void ParseReviewerOutput_changes_required()
    {
        var raw = """
            ```json
            {
              "decision": "ChangesRequired",
              "summary": "Several issues need attention",
              "mustFix": [
                { "description": "Missing null check", "filePath": "UserService.cs", "severity": "Error" },
                { "description": "SQL injection risk", "filePath": "UserRepo.cs", "severity": "Critical" }
              ],
              "securityConcerns": ["SQL injection in query builder"]
            }
            ```
            """;

        var result = _parser.ParseReviewerOutput(raw);

        Assert.True(result.Success);
        Assert.Equal(ReviewDecision.ChangesRequired, result.Value!.Decision);
        Assert.False(result.Value.CanMerge);
        Assert.Equal(2, result.Value.MustFix.Count);
    }

    [Fact]
    public void ParseReviewerOutput_approved_with_suggestions_can_merge()
    {
        var raw = """
            ```json
            {
              "decision": "ApprovedWithSuggestions",
              "summary": "Good work, minor suggestions"
            }
            ```
            """;

        var result = _parser.ParseReviewerOutput(raw);

        Assert.True(result.Success);
        Assert.True(result.Value!.CanMerge);
    }

    // ── Tester parsing ───────────────────────────────────────────────

    [Fact]
    public void ParseTesterOutput_all_passed()
    {
        var raw = """
            ```json
            {
              "decision": "AllPassed",
              "summary": "All 5 tests passed",
              "testsPassed": 5,
              "testsFailed": 0,
              "testsSkipped": 0
            }
            ```
            """;

        var result = _parser.ParseTesterOutput(raw);

        Assert.True(result.Success);
        Assert.Equal(TestDecision.AllPassed, result.Value!.Decision);
        Assert.Equal(5, result.Value.TestsPassed);
    }

    [Fact]
    public void ParseTesterOutput_some_failed()
    {
        var raw = """
            ```json
            {
              "decision": "SomeFailed",
              "summary": "3 out of 5 tests passed",
              "testsPassed": 3,
              "testsFailed": 2,
              "failures": [
                { "testName": "GetUsers_ReturnsEmpty", "reason": "Expected 0 but got 1", "isFlaky": false },
                { "testName": "GetUsers_Pagination", "reason": "Timeout", "isFlaky": true }
              ]
            }
            ```
            """;

        var result = _parser.ParseTesterOutput(raw);

        Assert.True(result.Success);
        Assert.Equal(TestDecision.SomeFailed, result.Value!.Decision);
        Assert.Equal(2, result.Value.Failures.Count);
        Assert.True(result.Value.Failures[1].IsFlaky);
    }

    // ── Edge cases ───────────────────────────────────────────────────

    [Fact]
    public void Parser_handles_bare_json_without_code_fence()
    {
        var raw = """
            {
              "action": "ProceedDirectly",
              "summary": "Simple task, proceeding immediately"
            }
            """;

        var result = _parser.ParseOrchestratorOutput(raw);

        Assert.True(result.Success);
        Assert.Equal(OrchestratorAction.ProceedDirectly, result.Value!.Action);
    }

    [Fact]
    public void Parser_handles_json_with_surrounding_text()
    {
        var raw = """
            Let me analyze this task.

            ```json
            {
              "action": "ProposePlan",
              "summary": "Here's my plan"
            }
            ```

            Let me know if you want changes.
            """;

        var result = _parser.ParseOrchestratorOutput(raw);
        Assert.True(result.Success);
    }

    [Fact]
    public void Parser_handles_cli_wrapped_json_with_line_continuations()
    {
        // Simulates Claude Code CLI output where long JSON values wrap across lines
        var raw = """
            ● Some CLI tool-use output here

            ```json
            {
              "action": "AskQuestions",
              "summary": "Task 'new version 3.0' lacks sufficient detail for analysis. Need clarification on
                project scope, current version, and upgrade requirements.",
              "chatMessage": "Hey team! I'm looking at this 'new version 3.0' task, but I need more context
                to create a solid implementation plan. Right now all I have is the title and a simple 'Hi' - not
                much to work with!",
              "questions": ["What is the current version?", "What are the key features planned?",
                "What technology stack are we using?"],
              "estimatedComplexity": "High"
            }
            ```

            Some trailing text
            """;

        var result = _parser.ParseOrchestratorOutput(raw);

        Assert.True(result.Success, $"Parsing should succeed. Error: {result.ErrorMessage}");
        Assert.Equal(OrchestratorAction.AskQuestions, result.Value!.Action);
        Assert.Contains("new version 3.0", result.Value.Summary);
        Assert.NotNull(result.Value.ChatMessage);
        Assert.Contains("Hey team", result.Value.ChatMessage!);
        Assert.True(result.Value.Questions.Count >= 2);
    }

    [Fact]
    public void Parser_handles_cli_wrapped_json_with_heavy_indentation()
    {
        // Simulates the exact indentation pattern from Claude Code output
        var raw = "● Analysis complete\r\n\r\n   ```json\r\n   {\r\n     \"action\": \"ProposePlan\",\r\n     \"summary\": \"Creating a modular prime number generator with different algorithms, comprehensive\r\n    testing, and documentation.\",\r\n     \"chatMessage\": \"Hey! Ik stel voor dat we een prime number generator bouwen met meerdere\r\n   algoritmes.\",\r\n     \"plan\": \"## Phase 1\\nSetup\",\r\n     \"estimatedComplexity\": \"Medium\"\r\n   }\r\n   ```\r\n\r\n";

        var result = _parser.ParseOrchestratorOutput(raw);

        Assert.True(result.Success, $"Parsing should succeed. Error: {result.ErrorMessage}");
        Assert.Equal(OrchestratorAction.ProposePlan, result.Value!.Action);
        Assert.Contains("prime number generator", result.Value.Summary);
    }

    [Fact]
    public void ParseOrchestratorOutput_handles_plan_as_object()
    {
        // Real-world v8.0 scenario: the LLM returns "plan" as a nested object instead of a string
        var raw = """
            ● ```json
               {
                 "action": "ProposePlan",
                 "summary": "Creating a C# prime number generator",
                 "chatMessage": "Hey team! Let's build a prime number generator.",
                 "plan": {
                   "phases": [
                     { "phase": "Implementation", "owner": "Sam" },
                     { "phase": "Review", "owner": "Morgan" }
                   ],
                   "timeline": "Demo session"
                 },
                 "questions": []
               }
               ```
            """;

        var result = _parser.ParseOrchestratorOutput(raw);

        Assert.True(result.Success, $"Should parse plan-as-object. Error: {result.ErrorMessage}");
        Assert.Equal(OrchestratorAction.ProposePlan, result.Value!.Action);
        Assert.NotNull(result.Value.Plan);
        // Plan is serialized back to a JSON string
        Assert.Contains("Implementation", result.Value.Plan!);
        Assert.Contains("Sam", result.Value.Plan);
    }

    [Fact]
    public void ParseOrchestratorOutput_handles_plan_as_string()
    {
        // Normal case: plan is a markdown string
        var raw = """
            ```json
            {
              "action": "ProposePlan",
              "summary": "Build feature",
              "plan": "## Phase 1\nSetup project\n## Phase 2\nImplement"
            }
            ```
            """;

        var result = _parser.ParseOrchestratorOutput(raw);

        Assert.True(result.Success);
        Assert.Contains("Phase 1", result.Value!.Plan!);
    }

    [Fact]
    public void Parser_handles_malformed_json_gracefully()
    {
        var raw = """
            ```json
            { "action": "ProposePlan", summary: broken }
            ```
            """;

        var result = _parser.ParseOrchestratorOutput(raw);
        Assert.False(result.Success);
        Assert.NotNull(result.FallbackSummary);
    }

    // ── CLI marker stripping ──────────────────────────────────────────

    [Fact]
    public void ParseOrchestratorOutput_strips_bullet_prefix_from_json()
    {
        var raw = """
            ● ```json
               {
                 "action": "ProposePlan",
                 "summary": "Build a prime generator",
                 "chatMessage": "Let's go team!",
                 "plan": "Step 1: Implement"
               }
               ```
            """;

        var result = _parser.ParseOrchestratorOutput(raw);

        Assert.True(result.Success, $"Should parse despite ● prefix. Error: {result.ErrorMessage}");
        Assert.Equal(OrchestratorAction.ProposePlan, result.Value!.Action);
        Assert.Equal("Build a prime generator", result.Value.Summary);
    }

    [Theory]
    [InlineData("● ")]
    [InlineData("✓ ")]
    [InlineData("✗ ")]
    [InlineData("↪ ")]
    [InlineData("$ ")]
    public void ParseOrchestratorOutput_strips_all_cli_marker_types(string marker)
    {
        var raw = $"{marker}```json\n{{\n  \"action\": \"ProceedDirectly\",\n  \"summary\": \"test\"\n}}\n```";

        var result = _parser.ParseOrchestratorOutput(raw);

        Assert.True(result.Success, $"Should parse despite '{marker.Trim()}' prefix. Error: {result.ErrorMessage}");
    }

    [Fact]
    public void ParseCoderOutput_strips_bullet_prefix()
    {
        var raw = """
            ● ```json
               {
                 "status": "Completed",
                 "summary": "Implemented the feature",
                 "canContinueToReview": true
               }
               ```
            """;

        var result = _parser.ParseCoderOutput(raw);

        Assert.True(result.Success, $"Should parse coder output despite ● prefix. Error: {result.ErrorMessage}");
        Assert.Equal(CoderStatus.Completed, result.Value!.Status);
    }

    [Fact]
    public void ParseReviewerOutput_strips_bullet_prefix()
    {
        var raw = """
            ● ```json
               {
                 "decision": "Approved",
                 "summary": "Clean code"
               }
               ```
            """;

        var result = _parser.ParseReviewerOutput(raw);

        Assert.True(result.Success, $"Should parse reviewer output despite ● prefix. Error: {result.ErrorMessage}");
        Assert.Equal(ReviewDecision.Approved, result.Value!.Decision);
    }

    [Fact]
    public void ParseTesterOutput_strips_bullet_prefix()
    {
        var raw = """
            ● ```json
               {
                 "decision": "AllPassed",
                 "summary": "All tests passed",
                 "testsPassed": 5,
                 "testsFailed": 0
               }
               ```
            """;

        var result = _parser.ParseTesterOutput(raw);

        Assert.True(result.Success, $"Should parse tester output despite ● prefix. Error: {result.ErrorMessage}");
        Assert.Equal(5, result.Value!.TestsPassed);
    }
}
