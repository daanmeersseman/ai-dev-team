using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AiDevTeam.Infrastructure.Services;

public class AgentResponseProcessor : IAgentResponseProcessor
{
    private readonly IMessageService _messageService;
    private readonly IArtifactService _artifactService;
    private readonly IContextBlockService _contextBlockService;
    private readonly StoragePaths _paths;
    private readonly ILogger<AgentResponseProcessor> _logger;

    public AgentResponseProcessor(
        IMessageService messageService,
        IArtifactService artifactService,
        IContextBlockService contextBlockService,
        StoragePaths paths,
        ILogger<AgentResponseProcessor> logger)
    {
        _messageService = messageService;
        _artifactService = artifactService;
        _contextBlockService = contextBlockService;
        _paths = paths;
        _logger = logger;
    }

    public async Task ProcessAgentResponseAsync(string conversationId, AgentDefinition agent, string response, string runId)
    {
        var messageType = AgentTextUtilities.GetMessageType(agent.Role);

        try
        {
            // Strip leading tool-use indicators (● ✓ etc.) that some providers emit
            var chatContent = CleanProviderOutput(response);

            // Always post the full response to chat — the user expects to see it there
            await _messageService.AddAsync(conversationId, agent.Name, agent.Role.ToString(),
                messageType, chatContent, agentRunId: runId);

            // Additionally save as artifact for reference and extract code blocks
            var hasDetailedContent = response.Length > 300 || response.Contains("```") || response.Contains("##");
            if (hasDetailedContent)
            {
                try
                {
                    var agentFirstName = agent.Name.Split(' ', '(')[0].Trim().ToLowerInvariant();
                    var artifactLabel = AgentTextUtilities.GetArtifactLabel(agent.Role);
                    var fileName = $"{agentFirstName}-{artifactLabel}.md";
                    await _artifactService.CreateAsync(conversationId, fileName, ArtifactType.Markdown, response, agent.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create artifact for {Agent}", agent.Name);
                }

                await ExtractAndSaveCodeArtifactsAsync(conversationId, agent, response, runId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process response from {Agent}", agent.Name);
            await _messageService.AddAsync(conversationId, agent.Name, agent.Role.ToString(),
                MessageType.StatusUpdate, "Task completed (response processing failed)", agentRunId: runId);
        }
    }

    public async Task ExtractAndSaveCodeArtifactsAsync(string conversationId, AgentDefinition agent, string response, string runId)
    {
        var codeBlockPattern = new Regex(@"```(\w+)?\s*\n(.*?)```", RegexOptions.Singleline);
        var matches = codeBlockPattern.Matches(response);
        var fileIndex = 0;

        foreach (Match match in matches)
        {
            var language = match.Groups[1].Value;
            var code = match.Groups[2].Value.Trim();

            if (string.IsNullOrWhiteSpace(code) || code.Length < 20) continue; // Skip tiny snippets

            fileIndex++;
            var ext = language.ToLowerInvariant() switch
            {
                "typescript" or "ts" => ".ts",
                "javascript" or "js" => ".js",
                "csharp" or "cs" or "c#" => ".cs",
                "python" or "py" => ".py",
                "java" => ".java",
                "sql" => ".sql",
                "json" => ".json",
                "html" => ".html",
                "css" => ".css",
                "rust" or "rs" => ".rs",
                "go" => ".go",
                "xml" => ".xml",
                "yaml" or "yml" => ".yaml",
                "bash" or "sh" or "shell" => ".sh",
                _ => ".txt"
            };

            var fileName = $"{agent.Name.Split(' ', '(')[0].Trim().ToLowerInvariant()}-output-{fileIndex}{ext}";
            var artifactType = ext == ".json" ? ArtifactType.Json : ArtifactType.Code;

            try
            {
                var artifact = await _artifactService.CreateAsync(conversationId, fileName, artifactType, code, agent.Name);
                _logger.LogInformation("Extracted code artifact {File} from {Agent} response", fileName, agent.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract artifact from {Agent} response", agent.Name);
            }
        }
    }

    public async Task UpdateAgentContextAsync(string conversationId, AgentDefinition agent, string prompt, string response, bool success)
    {
        try
        {
            var contextFile = _paths.AgentContextFile(conversationId, agent.Id);
            Directory.CreateDirectory(_paths.AgentContextDir(conversationId));

            var context = File.Exists(contextFile)
                ? await JsonStore.LoadAsync<AgentContext>(contextFile)
                : new AgentContext { AgentId = agent.Id, ConversationId = conversationId };

            context.RunCount++;
            context.LastUpdatedAt = DateTime.UtcNow;

            var outputSummary = success
                ? AgentTextUtilities.TruncateToOneLine(response, 150)
                : $"FAILED: {AgentTextUtilities.TruncateToOneLine(response, 100)}";

            var summaryLines = string.IsNullOrEmpty(context.Summary)
                ? new List<string>()
                : context.Summary.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            summaryLines.Add($"Run #{context.RunCount}: {outputSummary}");

            var maxChars = agent.MaxContextTokens * 4;
            while (summaryLines.Count > 1 && string.Join("\n", summaryLines).Length > maxChars)
                summaryLines.RemoveAt(0);

            context.Summary = string.Join("\n", summaryLines);
            context.EstimatedTokensUsed += AgentTextUtilities.EstimateTokens(prompt) + AgentTextUtilities.EstimateTokens(response);

            if (success)
            {
                var decisions = response
                    .Split('\n')
                    .Where(l => l.TrimStart().StartsWith("- ") || l.TrimStart().StartsWith("* "))
                    .Select(l => l.Trim().TrimStart('-', '*', ' '))
                    .Where(l => l.Length > 10 && l.Length < 200)
                    .Take(3)
                    .ToList();

                if (decisions.Any())
                {
                    context.KeyDecisions.AddRange(decisions);
                    if (context.KeyDecisions.Count > 10)
                        context.KeyDecisions = context.KeyDecisions.TakeLast(10).ToList();
                }
            }

            await JsonStore.SaveAsync(contextFile, context);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update agent context for {AgentName}", agent.Name);
        }
    }

    /// <summary>
    /// Strips CLI/tool-use artefacts (● ✓ $ arrows etc.) that some providers emit,
    /// keeping only the human-readable prose and code blocks.
    /// </summary>
    private static string CleanProviderOutput(string raw)
    {
        var lines = raw.Split('\n');
        var cleaned = new List<string>();
        var inCodeBlock = false;

        foreach (var line in lines)
        {
            // Track code fences so we don't strip content inside them
            if (line.TrimStart().StartsWith("```"))
                inCodeBlock = !inCodeBlock;

            if (inCodeBlock)
            {
                cleaned.Add(line);
                continue;
            }

            var trimmed = line.TrimStart();
            // Skip lines that look like CLI tool-use output
            if (trimmed.StartsWith("✓ ") || trimmed.StartsWith("$ ") || trimmed.StartsWith("↪ "))
                continue;

            // Strip leading ● bullet that Claude Code prefixes on prose
            if (trimmed.StartsWith("● "))
            {
                cleaned.Add(line.Replace("● ", ""));
                continue;
            }

            cleaned.Add(line);
        }

        return string.Join("\n", cleaned).Trim();
    }

    private static string ExtractBriefChatMessage(string response)
    {
        var lines = response.Split('\n');
        var chatLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Stop at code blocks, markdown headers, horizontal rules, blank lines after first sentence
            if (trimmed.StartsWith("```") || trimmed.StartsWith("##") || trimmed.StartsWith("---"))
                break;
            if (chatLines.Count > 0 && string.IsNullOrWhiteSpace(trimmed))
                break;
            // Skip top-level markdown headers
            if (trimmed.StartsWith("# ")) continue;
            // Skip bullet points and structured content
            if (trimmed.StartsWith("- **") || trimmed.StartsWith("| ")) continue;

            chatLines.Add(trimmed);
            if (chatLines.Count >= 3) break;
        }

        var result = chatLines.Any() ? string.Join(" ", chatLines).Trim() : "Done -- check the artifacts for details.";

        // Cap at ~250 chars
        if (result.Length > 250)
        {
            var cutoff = result.LastIndexOf('.', 250);
            if (cutoff > 100) result = result[..(cutoff + 1)];
            else result = result[..247] + "...";
        }

        return result;
    }

    private static string ExtractChatMessage(string fullResponse, string blockId, string contextLabel)
    {
        var lines = fullResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Take the first paragraph (up to first blank line or heading) as the chat message
        var chatLines = new List<string>();
        foreach (var line in lines)
        {
            if (chatLines.Count > 0 && (line.StartsWith("##") || line.StartsWith("```") || string.IsNullOrWhiteSpace(line)))
                break;
            if (!line.StartsWith("# ")) // Skip top-level headers
                chatLines.Add(line);
            if (chatLines.Count >= 4) break;
        }

        var chatPart = chatLines.Any()
            ? string.Join("\n", chatLines).Trim()
            : "Here's what I found.";

        return $"{chatPart}\n\nSee [[block:{blockId}:{contextLabel}]] for the full details.";
    }
}
