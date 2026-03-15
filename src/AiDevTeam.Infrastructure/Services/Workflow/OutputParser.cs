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

        // Try parsing the entire output as JSON
        var trimmed = rawOutput.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            try
            {
                var result = JsonSerializer.Deserialize<T>(trimmed, JsonOptions);
                if (result != null)
                    return ParseResult<T>.Ok(result, rawOutput);
            }
            catch (JsonException)
            {
                // Fall through to fallback
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
        // Strip CLI markers (● ✓ ✗ $ ↪) that providers prefix on output lines.
        // These prevent the ```json fence regex from matching.
        var cleaned = StripCliMarkers(text);

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
    /// Strips CLI tool-use markers (● ✓ ✗ $ ↪) from the start of lines
    /// so that code fences like ```json are not obscured.
    /// </summary>
    private static string StripCliMarkers(string text)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("● "))
                lines[i] = trimmed[2..];
            else if (trimmed.StartsWith("✓ ") || trimmed.StartsWith("✗ ") || trimmed.StartsWith("↪ "))
                lines[i] = trimmed[2..];
            else if (trimmed.StartsWith("$ "))
                lines[i] = trimmed[2..];
        }
        return string.Join('\n', lines);
    }

    [GeneratedRegex(@"```json\s*\n([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex JsonFenceRegex();

    [GeneratedRegex(@"```\s*\n([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex BareFenceRegex();
}
