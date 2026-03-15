using AiDevTeam.Core.Models;
using System.Text.RegularExpressions;

namespace AiDevTeam.Infrastructure.Services;

public static class AgentTextUtilities
{
    public static MessageType GetMessageType(AgentRole role) => role switch
    {
        AgentRole.Orchestrator => MessageType.Plan,
        AgentRole.Reviewer => MessageType.Review,
        AgentRole.Tester => MessageType.TestResult,
        _ => MessageType.AgentThoughtSummary
    };

    public static string GetArtifactLabel(AgentRole role) => role switch
    {
        AgentRole.Orchestrator => "analysis",
        AgentRole.Reviewer => "review",
        AgentRole.Coder => "implementation",
        AgentRole.Tester => "tests",
        AgentRole.DatabaseSpecialist => "db-analysis",
        _ => "output"
    };

    public static string GetContextLabel(AgentRole role) => role switch
    {
        AgentRole.Orchestrator => "task analysis",
        AgentRole.Reviewer => "code review",
        AgentRole.Coder => "implementation details",
        AgentRole.Tester => "test results",
        AgentRole.DatabaseSpecialist => "DB analysis",
        _ => "detailed analysis"
    };

    public static string GetHandoffAction(AgentRole from, AgentRole to) => (from, to) switch
    {
        (AgentRole.Orchestrator, AgentRole.Coder) => "implementation",
        (AgentRole.Orchestrator, AgentRole.Reviewer) => "code review",
        (AgentRole.Orchestrator, AgentRole.Tester) => "testing",
        (AgentRole.Orchestrator, AgentRole.DatabaseSpecialist) => "DB analysis",
        (AgentRole.Coder, AgentRole.Reviewer) => "code review",
        (AgentRole.Reviewer, AgentRole.Coder) => "addressing review feedback",
        (AgentRole.Reviewer, AgentRole.Tester) => "testing",
        (AgentRole.Tester, AgentRole.Coder) => "fixing test failures",
        (AgentRole.Coder, AgentRole.Tester) => "testing the changes",
        _ => "follow-up"
    };

    public static ArtifactType GetArtifactType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".md" => ArtifactType.Markdown,
            ".json" => ArtifactType.Json,
            ".cs" or ".js" or ".ts" or ".py" or ".sql" => ArtifactType.Code,
            ".log" or ".txt" => ArtifactType.Log,
            ".patch" or ".diff" => ArtifactType.Patch,
            ".png" or ".jpg" or ".gif" => ArtifactType.Image,
            _ => ArtifactType.Other
        };
    }

    public static string TruncateToOneLine(string text, int maxLen)
    {
        var line = text.Replace("\r\n", " ").Replace("\n", " ").Trim();
        return line.Length > maxLen ? line[..maxLen] + "..." : line;
    }

    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)(text.Length / 4.0 * 1.3);
    }

    public static bool ContainsTechnicalContent(string response)
    {
        return response.Contains("```")
            || response.Contains("##")
            || response.Contains("| ")
            || Regex.IsMatch(response, @"^\d+\.\s", RegexOptions.Multiline);
    }

    public static bool ContainsDelegationLanguage(string response)
    {
        var patterns = new[]
        {
            "should handle", "can take care of", "will work on", "assign this to",
            "let me delegate", "I'll have", "pass this to", "hand this off",
            "should look at", "needs to review", "can implement", "should write"
        };
        var lower = response.ToLowerInvariant();
        return patterns.Any(p => lower.Contains(p));
    }
}
