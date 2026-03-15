using AiDevTeam.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiDevTeam.Infrastructure.Services;

public class GitHubIssueService : IGitHubIssueService
{
    private readonly ILogger<GitHubIssueService> _logger;

    public GitHubIssueService(ILogger<GitHubIssueService> logger) => _logger = logger;

    public async Task<GitHubIssueInfo?> FetchIssueAsync(string url)
    {
        // Parse GitHub issue URL: https://github.com/{owner}/{repo}/issues/{number}
        var match = Regex.Match(url.Trim(), @"github\.com/([^/]+/[^/]+)/issues/(\d+)");
        if (!match.Success)
            return null;

        var repo = match.Groups[1].Value;
        var number = int.Parse(match.Groups[2].Value);

        try
        {
            var result = await RunCommandAsync("gh", $"issue view {number} --repo {repo} --json title,body,state,labels,assignees", 15);
            if (result.exitCode != 0)
            {
                _logger.LogWarning("gh issue view failed: {Error}", result.output);
                return null;
            }

            using var doc = JsonDocument.Parse(result.output);
            var root = doc.RootElement;

            var info = new GitHubIssueInfo
            {
                Url = url.Trim(),
                Repository = repo,
                Number = number,
                Title = root.GetProperty("title").GetString() ?? "",
                Body = root.GetProperty("body").GetString() ?? "",
                State = root.GetProperty("state").GetString() ?? "OPEN"
            };

            if (root.TryGetProperty("labels", out var labels))
            {
                foreach (var label in labels.EnumerateArray())
                {
                    var name = label.GetProperty("name").GetString();
                    if (name != null) info.Labels.Add(name);
                }
            }

            if (root.TryGetProperty("assignees", out var assignees) && assignees.GetArrayLength() > 0)
            {
                info.Assignee = assignees[0].GetProperty("login").GetString();
            }

            return info;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch GitHub issue from {Url}", url);
            return null;
        }
    }

    public async Task<string> AnalyzeIssueAsync(GitHubIssueInfo issue)
    {
        var prompt = $"""
            Analyze the following GitHub issue and provide a concise technical summary.
            Include: what needs to be done, potential approach, complexity estimate (low/medium/high), and any concerns.
            Keep it under 300 words. Format in markdown.

            Repository: {issue.Repository}
            Issue #{issue.Number}: {issue.Title}
            Labels: {string.Join(", ", issue.Labels)}

            {issue.Body}
            """;

        // Try copilot CLI first, then claude CLI as fallback
        var result = await TryRunAnalysis("copilot", $"-p \"{EscapeForCmd(prompt)}\"", 60);
        if (result == null)
            result = await TryRunAnalysis("claude", $"-p \"{EscapeForCmd(prompt)}\" --no-input", 60);

        return result ?? $"""
            ## Issue Analysis: {issue.Title}

            **Repository:** {issue.Repository}
            **Issue:** #{issue.Number}
            **State:** {issue.State}
            **Labels:** {string.Join(", ", issue.Labels)}

            ### Description
            {(issue.Body.Length > 500 ? issue.Body[..500] + "..." : issue.Body)}

            *Automated analysis unavailable — no CLI tool could process this issue.*
            """;
    }

    private async Task<string?> TryRunAnalysis(string executable, string arguments, int timeoutSeconds)
    {
        try
        {
            var result = await RunCommandAsync(executable, arguments, timeoutSeconds);
            if (result.exitCode == 0 && !string.IsNullOrWhiteSpace(result.output))
                return result.output.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Analysis with {Tool} failed: {Error}", executable, ex.Message);
        }
        return null;
    }

    private static string EscapeForCmd(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
    }

    private static async Task<(int exitCode, string output)> RunCommandAsync(string executable, string arguments, int timeoutSeconds)
    {
        using var process = new Process();

        if (OperatingSystem.IsWindows())
        {
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {executable} {arguments}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
        }
        else
        {
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
        }

        process.Start();
        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch
        {
            try { process.Kill(true); } catch { }
            return (-1, "Timeout");
        }

        var output = await outputTask;
        var error = await errorTask;

        return (process.ExitCode, string.IsNullOrEmpty(output) ? error : output);
    }
}
