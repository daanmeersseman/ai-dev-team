using AiDevTeam.Core.Interfaces;

namespace AiDevTeam.Providers;

public class MockProvider : IAgentProvider
{
    public string ProviderType => "Mock";

    public async Task<AgentRunResult> ExecuteAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Simulate processing time
        await Task.Delay(Random.Shared.Next(1000, 3000), cancellationToken);

        request.OnOutputReceived?.Invoke("Processing prompt...");
        await Task.Delay(500, cancellationToken);
        request.OnOutputReceived?.Invoke("Analyzing requirements...");
        await Task.Delay(500, cancellationToken);
        request.OnOutputReceived?.Invoke("Generating response...");
        await Task.Delay(500, cancellationToken);

        sw.Stop();

        var response = GenerateMockResponse(request.Prompt, request.SystemPrompt);

        return new AgentRunResult
        {
            Success = true,
            Output = response.Output,
            ExitCode = 0,
            DurationMs = sw.ElapsedMilliseconds,
            Artifacts = response.Artifacts
        };
    }

    private static (string Output, List<AgentArtifactOutput> Artifacts) GenerateMockResponse(string prompt, string? systemPrompt)
    {
        var role = systemPrompt?.Contains("orchestrator", StringComparison.OrdinalIgnoreCase) == true ? "orchestrator"
            : systemPrompt?.Contains("reviewer", StringComparison.OrdinalIgnoreCase) == true ? "reviewer"
            : systemPrompt?.Contains("coder", StringComparison.OrdinalIgnoreCase) == true ? "coder"
            : systemPrompt?.Contains("tester", StringComparison.OrdinalIgnoreCase) == true ? "tester"
            : systemPrompt?.Contains("database", StringComparison.OrdinalIgnoreCase) == true ? "database"
            : "agent";

        var artifacts = new List<AgentArtifactOutput>();

        var output = role switch
        {
            "orchestrator" => $"## Task Analysis\n\nI've analyzed the request: *{Truncate(prompt, 100)}*\n\n### Breakdown\n1. **Requirements gathering** — Need to clarify scope\n2. **Implementation plan** — Will assign to Coder agent\n3. **Review cycle** — Senior Reviewer will validate\n4. **Testing** — Tester agent will verify\n\n### Recommendation\nProceeding with implementation. Assigning sub-tasks to team members.",

            "reviewer" => $"## Code Review\n\nReviewed the changes for: *{Truncate(prompt, 80)}*\n\n### Findings\n- **Architecture**: Looks good, follows established patterns\n- **Code quality**: Clean and readable\n- **Potential issues**: None critical found\n- **Suggestions**: Consider adding error handling for edge cases\n\n### Verdict\n✅ **Approved** with minor suggestions",

            "coder" => GenerateCoderResponse(prompt, artifacts),

            "tester" => $"## Test Results\n\nExecuted test suite for: *{Truncate(prompt, 80)}*\n\n### Summary\n- **Total tests**: 12\n- **Passed**: 11 ✅\n- **Failed**: 1 ❌\n- **Skipped**: 0\n\n### Failed Test\n`TestEdgeCaseHandling` — Expected null check not present\n\n### Coverage\nLine coverage: 87%\nBranch coverage: 74%",

            "database" => $"## Database Impact Analysis\n\nAnalyzed for: *{Truncate(prompt, 80)}*\n\n### Changes Required\n- No schema migrations needed\n- Index optimization recommended on `Messages.ConversationId`\n- Query performance: estimated < 50ms for typical load\n\n### Recommendations\n- Add composite index on `(ConversationId, CreatedAt)`\n- Consider partitioning if conversations exceed 100k",

            _ => $"## Agent Response\n\nProcessed request: *{Truncate(prompt, 100)}*\n\nTask completed successfully. No issues found."
        };

        return (output, artifacts);
    }

    private static string GenerateCoderResponse(string prompt, List<AgentArtifactOutput> artifacts)
    {
        artifacts.Add(new AgentArtifactOutput
        {
            FileName = "implementation-notes.md",
            Content = $"# Implementation Notes\n\n## Task\n{prompt}\n\n## Approach\n- Followed existing patterns in the codebase\n- Added proper error handling\n- Ensured backward compatibility\n\n## Files Modified\n- `Service.cs` — Added new method\n- `Controller.cs` — Added endpoint\n\n## Notes\nReady for review."
        });

        return $"## Implementation Complete\n\nImplemented changes for: *{Truncate(prompt, 80)}*\n\n### Changes Made\n- Added new service method with proper validation\n- Updated API endpoint\n- Added unit tests\n\n### Files\n- `implementation-notes.md` — Detailed notes attached\n\nReady for review by Senior Reviewer.";
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "...";
}
