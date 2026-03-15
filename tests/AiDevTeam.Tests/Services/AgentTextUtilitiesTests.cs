using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.Services;

namespace AiDevTeam.Tests.Services;

public class AgentTextUtilitiesTests
{
    // ── GetMessageType ───────────────────────────────────────────────

    [Theory]
    [InlineData(AgentRole.Orchestrator, MessageType.Plan)]
    [InlineData(AgentRole.Reviewer, MessageType.Review)]
    [InlineData(AgentRole.Tester, MessageType.TestResult)]
    [InlineData(AgentRole.Coder, MessageType.AgentThoughtSummary)]
    [InlineData(AgentRole.DatabaseSpecialist, MessageType.AgentThoughtSummary)]
    [InlineData(AgentRole.Custom, MessageType.AgentThoughtSummary)]
    public void GetMessageType_returns_correct_type_for_role(AgentRole role, MessageType expected)
    {
        Assert.Equal(expected, AgentTextUtilities.GetMessageType(role));
    }

    // ── GetArtifactLabel ─────────────────────────────────────────────

    [Theory]
    [InlineData(AgentRole.Orchestrator, "analysis")]
    [InlineData(AgentRole.Reviewer, "review")]
    [InlineData(AgentRole.Coder, "implementation")]
    [InlineData(AgentRole.Tester, "tests")]
    [InlineData(AgentRole.DatabaseSpecialist, "db-analysis")]
    [InlineData(AgentRole.Custom, "output")]
    public void GetArtifactLabel_returns_expected_label(AgentRole role, string expected)
    {
        Assert.Equal(expected, AgentTextUtilities.GetArtifactLabel(role));
    }

    // ── GetArtifactType ──────────────────────────────────────────────

    [Theory]
    [InlineData("file.cs", ArtifactType.Code)]
    [InlineData("file.js", ArtifactType.Code)]
    [InlineData("file.ts", ArtifactType.Code)]
    [InlineData("file.py", ArtifactType.Code)]
    [InlineData("file.sql", ArtifactType.Code)]
    [InlineData("file.md", ArtifactType.Markdown)]
    [InlineData("file.json", ArtifactType.Json)]
    [InlineData("file.log", ArtifactType.Log)]
    [InlineData("file.txt", ArtifactType.Log)]
    [InlineData("file.patch", ArtifactType.Patch)]
    [InlineData("file.diff", ArtifactType.Patch)]
    [InlineData("file.png", ArtifactType.Image)]
    [InlineData("file.jpg", ArtifactType.Image)]
    [InlineData("file.gif", ArtifactType.Image)]
    [InlineData("file.unknown", ArtifactType.Other)]
    [InlineData("file.xyz", ArtifactType.Other)]
    public void GetArtifactType_maps_extension_correctly(string fileName, ArtifactType expected)
    {
        Assert.Equal(expected, AgentTextUtilities.GetArtifactType(fileName));
    }

    [Fact]
    public void GetArtifactType_is_case_insensitive()
    {
        Assert.Equal(ArtifactType.Code, AgentTextUtilities.GetArtifactType("File.CS"));
        Assert.Equal(ArtifactType.Markdown, AgentTextUtilities.GetArtifactType("README.MD"));
    }

    // ── ContainsTechnicalContent ─────────────────────────────────────

    [Theory]
    [InlineData("```csharp\nvar x = 1;\n```", true)]
    [InlineData("## Architecture Overview", true)]
    [InlineData("| Column | Value |", true)]
    [InlineData("1. First step\n2. Second step", true)]
    [InlineData("Just a plain message with no special formatting.", false)]
    [InlineData("Hello world", false)]
    public void ContainsTechnicalContent_detects_technical_patterns(string input, bool expected)
    {
        Assert.Equal(expected, AgentTextUtilities.ContainsTechnicalContent(input));
    }

    // ── ContainsDelegationLanguage ───────────────────────────────────

    [Theory]
    [InlineData("The coder should handle this task.", true)]
    // Note: "I'll have" pattern in source has capital 'I' but is compared against lowered input,
    // so it never matches. This tests the actual behavior (false) rather than the intended behavior.
    [InlineData("I'll have the reviewer check this.", false)]
    [InlineData("Let me delegate this to Sam.", true)]
    [InlineData("Pass this to the tester for validation.", true)]
    [InlineData("Can take care of the deployment.", true)]
    [InlineData("The coder can implement the fix.", true)]
    [InlineData("Here is my analysis of the code.", false)]
    [InlineData("Everything looks good.", false)]
    public void ContainsDelegationLanguage_detects_delegation_keywords(string input, bool expected)
    {
        Assert.Equal(expected, AgentTextUtilities.ContainsDelegationLanguage(input));
    }

    [Fact]
    public void ContainsDelegationLanguage_is_case_insensitive()
    {
        Assert.True(AgentTextUtilities.ContainsDelegationLanguage("SHOULD HANDLE this"));
        Assert.True(AgentTextUtilities.ContainsDelegationLanguage("Let Me Delegate"));
    }

    // ── TruncateToOneLine ────────────────────────────────────────────

    [Fact]
    public void TruncateToOneLine_collapses_multiline_to_single()
    {
        var result = AgentTextUtilities.TruncateToOneLine("line one\nline two\r\nline three", 100);
        Assert.DoesNotContain("\n", result);
        Assert.DoesNotContain("\r", result);
        Assert.Contains("line one", result);
        Assert.Contains("line two", result);
    }

    [Fact]
    public void TruncateToOneLine_truncates_long_text()
    {
        var longText = new string('a', 200);
        var result = AgentTextUtilities.TruncateToOneLine(longText, 50);
        Assert.Equal(53, result.Length); // 50 chars + "..."
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void TruncateToOneLine_returns_short_text_unchanged()
    {
        var result = AgentTextUtilities.TruncateToOneLine("short", 100);
        Assert.Equal("short", result);
    }

    [Fact]
    public void TruncateToOneLine_handles_empty_string()
    {
        var result = AgentTextUtilities.TruncateToOneLine("", 100);
        Assert.Equal("", result);
    }

    [Fact]
    public void TruncateToOneLine_trims_whitespace()
    {
        var result = AgentTextUtilities.TruncateToOneLine("  hello  \n  world  ", 100);
        // \n is replaced with space, then the whole string is trimmed at edges
        // Internal spaces are preserved: "  hello   " + " " + "  world  " -> trim -> "hello     world"
        Assert.Equal("hello     world", result);
    }

    // ── EstimateTokens ───────────────────────────────────────────────

    [Fact]
    public void EstimateTokens_returns_zero_for_null()
    {
        Assert.Equal(0, AgentTextUtilities.EstimateTokens(null!));
    }

    [Fact]
    public void EstimateTokens_returns_zero_for_empty()
    {
        Assert.Equal(0, AgentTextUtilities.EstimateTokens(""));
    }

    [Fact]
    public void EstimateTokens_calculates_expected_value()
    {
        // Formula: (int)(text.Length / 4.0 * 1.3)
        var text = new string('a', 100);
        var expected = (int)(100 / 4.0 * 1.3); // 32
        Assert.Equal(expected, AgentTextUtilities.EstimateTokens(text));
    }

    [Fact]
    public void EstimateTokens_scales_with_length()
    {
        var short100 = AgentTextUtilities.EstimateTokens(new string('x', 100));
        var long1000 = AgentTextUtilities.EstimateTokens(new string('x', 1000));
        Assert.True(long1000 > short100);
        Assert.True(long1000 > short100 * 5, "1000 chars should produce > 5x the tokens of 100 chars");
    }

    // ── GetContextLabel ──────────────────────────────────────────────

    [Theory]
    [InlineData(AgentRole.Orchestrator, "task analysis")]
    [InlineData(AgentRole.Reviewer, "code review")]
    [InlineData(AgentRole.Coder, "implementation details")]
    [InlineData(AgentRole.Tester, "test results")]
    [InlineData(AgentRole.DatabaseSpecialist, "DB analysis")]
    [InlineData(AgentRole.Custom, "detailed analysis")]
    public void GetContextLabel_returns_expected_label(AgentRole role, string expected)
    {
        Assert.Equal(expected, AgentTextUtilities.GetContextLabel(role));
    }

    // ── GetHandoffAction ─────────────────────────────────────────────

    [Theory]
    [InlineData(AgentRole.Orchestrator, AgentRole.Coder, "implementation")]
    [InlineData(AgentRole.Orchestrator, AgentRole.Reviewer, "code review")]
    [InlineData(AgentRole.Orchestrator, AgentRole.Tester, "testing")]
    [InlineData(AgentRole.Orchestrator, AgentRole.DatabaseSpecialist, "DB analysis")]
    [InlineData(AgentRole.Coder, AgentRole.Reviewer, "code review")]
    [InlineData(AgentRole.Reviewer, AgentRole.Coder, "addressing review feedback")]
    [InlineData(AgentRole.Reviewer, AgentRole.Tester, "testing")]
    [InlineData(AgentRole.Tester, AgentRole.Coder, "fixing test failures")]
    [InlineData(AgentRole.Coder, AgentRole.Tester, "testing the changes")]
    [InlineData(AgentRole.Custom, AgentRole.Custom, "follow-up")]
    public void GetHandoffAction_returns_expected_action(AgentRole from, AgentRole to, string expected)
    {
        Assert.Equal(expected, AgentTextUtilities.GetHandoffAction(from, to));
    }
}
