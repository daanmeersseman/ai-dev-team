using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AiDevTeam.Core.Contracts;
using AiDevTeam.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AiDevTeam.Infrastructure.Services.Workflow;

public partial class OutputParser : IOutputParser
{
    private readonly ILogger<OutputParser> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OutputParser(ILogger<OutputParser> logger)
    {
        _logger = logger;
    }

    public ParseResult<OrchestratorResult> ParseOrchestratorOutput(string rawOutput)
        => ParseStructured<OrchestratorResult>(rawOutput, "orchestrator");

    public ParseResult<CoderResult> ParseCoderOutput(string rawOutput)
        => ParseStructured<CoderResult>(rawOutput, "coder");

    public ParseResult<ReviewResult> ParseReviewerOutput(string rawOutput)
        => ParseStructured<ReviewResult>(rawOutput, "reviewer");

    public ParseResult<TesterResult> ParseTesterOutput(string rawOutput)
        => ParseStructured<TesterResult>(rawOutput, "tester");

    private ParseResult<T> ParseStructured<T>(string rawOutput, string roleName) where T : class
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
            return ParseResult<T>.Fail("Empty output received", rawOutput);

        // Try to extract JSON from markdown code fences
        var json = ExtractJsonBlock(rawOutput);
        if (json != null)
        {
            try
            {
                var result = JsonSerializer.Deserialize<T>(json, JsonOptions);
                if (result != null)
                {
                    _logger.LogDebug("Successfully parsed {Role} output as structured JSON", roleName);
                    return ParseResult<T>.Ok(result, rawOutput);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "JSON found but failed to deserialize {Role} output", roleName);
            }
        }

        // Try parsing the entire output as JSON (after full CLI cleaning)
        var cleaned = AgentResponseProcessor.CleanProviderOutput(rawOutput);
        var trimmed = cleaned.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            try
            {
                var normalized = NormalizeCliJson(trimmed);
                var result = JsonSerializer.Deserialize<T>(normalized, JsonOptions);
                if (result != null)
                    return ParseResult<T>.Ok(result, rawOutput);
            }
            catch (JsonException)
            {
                // Fall through to fallback
            }
        }

        // Orchestrator-specific fallback: construct a result from free-text prose
        if (typeof(T) == typeof(OrchestratorResult))
        {
            var fallback = BuildOrchestratorFallback(cleaned);
            if (fallback != null)
            {
                _logger.LogInformation("Built fallback OrchestratorResult from free-text output (action: {Action})", fallback.Action);
                return ParseResult<T>.Ok((T)(object)fallback, rawOutput);
            }
        }

        // Fallback: create a summary from the raw text
        var summary = CreateFallbackSummary(rawOutput);
        _logger.LogWarning("Could not parse {Role} output as structured JSON, using fallback summary", roleName);
        return ParseResult<T>.Fail(
            $"Could not parse {roleName} output as structured JSON. Raw output did not contain valid JSON.",
            rawOutput,
            summary);
    }

    private static string? ExtractJsonBlock(string text)
    {
        // Use full CLI output cleaning to strip tool-use blocks, markers, and garbled Unicode.
        // The old StripCliMarkers only handled single-char prefixes; CleanProviderOutput removes
        // entire tool-use blocks (✓ List directory...\n  ↪ 14 items...) that break fence detection.
        var cleaned = AgentResponseProcessor.CleanProviderOutput(text);

        // Match ```json ... ``` or ``` ... ```
        var match = JsonFenceRegex().Match(cleaned);
        string? raw = null;

        if (match.Success)
        {
            raw = match.Groups[1].Value.Trim();
        }
        else
        {
            match = BareFenceRegex().Match(cleaned);
            if (match.Success)
            {
                var content = match.Groups[1].Value.Trim();
                if (content.StartsWith('{'))
                    raw = content;
            }
        }

        if (raw == null) return null;

        // Normalize JSON that may have been line-wrapped by the CLI provider.
        // CLI tools like Claude Code wrap long lines for display, breaking JSON
        // string values across multiple lines. Since JSON string values use
        // explicit \n escape sequences for newlines, actual newline characters
        // inside the JSON block are always formatting artifacts and safe to collapse.
        return NormalizeCliJson(raw);
    }

    /// <summary>
    /// Collapses CLI line-wrapping artifacts in extracted JSON text.
    /// JSON string values use explicit \n escape sequences for intentional newlines,
    /// so actual newline characters in the text are always display-wrapping artifacts
    /// and safe to collapse into spaces.
    /// </summary>
    private static string NormalizeCliJson(string json)
    {
        // Replace actual newlines (and surrounding whitespace) with a single space,
        // then collapse multiple spaces into one
        var collapsed = json
            .Replace("\r\n", " ")
            .Replace('\n', ' ')
            .Replace('\r', ' ');

        // Collapse runs of whitespace
        return CollapseWhitespace(collapsed).Trim();
    }

    private static string CollapseWhitespace(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var prevWasSpace = false;
        foreach (var c in text)
        {
            if (c == ' ')
            {
                if (!prevWasSpace)
                    sb.Append(c);
                prevWasSpace = true;
            }
            else
            {
                sb.Append(c);
                prevWasSpace = false;
            }
        }
        return sb.ToString();
    }

    private static string CreateFallbackSummary(string rawOutput)
    {
        // Take first ~500 chars as a summary
        var lines = rawOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var summary = string.Join(" ", lines.Take(5)).Trim();
        return summary.Length > 500 ? summary[..500] + "..." : summary;
    }

    /// <summary>
    /// Fallback: when the orchestrator ignores the JSON format and returns free-text prose,
    /// construct a usable OrchestratorResult from the text content so the workflow doesn't stall.
    /// </summary>
    private OrchestratorResult? BuildOrchestratorFallback(string cleanedText)
    {
        if (string.IsNullOrWhiteSpace(cleanedText) || cleanedText.Length < 20)
            return null;

        // Use the cleaned text as the chat message
        var chatMessage = cleanedText.Length > 2000 ? cleanedText[..2000] + "\n...(truncated)" : cleanedText;

        // Detect if this looks like a plan with team assignments
        var lowerText = cleanedText.ToLowerInvariant();
        var hasPlanSignals = lowerText.Contains("implement") || lowerText.Contains("test") ||
                             lowerText.Contains("review") || lowerText.Contains("team assignment") ||
                             lowerText.Contains("approach:") || lowerText.Contains("plan:");

        // Try to extract planned steps from mentions of agent roles
        var steps = new List<PlannedStep>();
        var rolePatterns = new Dictionary<string, string>
        {
            { "coder", "Coder" }, { "sam", "Coder" },
            { "reviewer", "Reviewer" }, { "morgan", "Reviewer" },
            { "tester", "Tester" }, { "riley", "Tester" },
            { "database", "DatabaseSpecialist" }, { "jordan", "DatabaseSpecialist" }
        };

        var foundRoles = new HashSet<string>();
        foreach (var (pattern, role) in rolePatterns)
        {
            if (lowerText.Contains(pattern) && foundRoles.Add(role))
            {
                steps.Add(new PlannedStep
                {
                    Order = steps.Count + 1,
                    AgentRole = role,
                    Description = $"Execute {role.ToLowerInvariant()} tasks as described in the plan",
                    IsOptional = role == "DatabaseSpecialist"
                });
            }
        }

        if (hasPlanSignals && steps.Count > 0)
        {
            _logger.LogInformation("Fallback: detected plan with {StepCount} agent assignments from free-text", steps.Count);
            return new OrchestratorResult
            {
                Action = OrchestratorAction.ProposePlan,
                Summary = CreateFallbackSummary(cleanedText),
                ChatMessage = chatMessage,
                Plan = cleanedText,
                PlannedSteps = steps
            };
        }

        // No plan detected — treat as a chat response so the user at least sees the output
        return new OrchestratorResult
        {
            Action = OrchestratorAction.ChatResponse,
            Summary = CreateFallbackSummary(cleanedText),
            ChatMessage = chatMessage
        };
    }

    [GeneratedRegex(@"```json\s*\n([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex JsonFenceRegex();

    [GeneratedRegex(@"```\s*\n([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex BareFenceRegex();
}
