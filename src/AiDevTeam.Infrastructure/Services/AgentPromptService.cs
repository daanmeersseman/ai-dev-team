using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace AiDevTeam.Infrastructure.Services;

public class AgentPromptService : IAgentPromptService
{
    private readonly IAgentDefinitionService _agentDefinitionService;
    private readonly IMessageService _messageService;
    private readonly IConversationService _conversationService;
    private readonly ITeamService _teamService;
    private readonly StoragePaths _paths;
    private readonly ILogger<AgentPromptService> _logger;

    public AgentPromptService(
        IAgentDefinitionService agentDefinitionService,
        IMessageService messageService,
        IConversationService conversationService,
        ITeamService teamService,
        StoragePaths paths,
        ILogger<AgentPromptService> logger)
    {
        _agentDefinitionService = agentDefinitionService;
        _messageService = messageService;
        _conversationService = conversationService;
        _teamService = teamService;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Build system prompt that includes team roster and communication instructions.
    /// </summary>
    public Task<string> BuildSystemPromptAsync(string conversationId, AgentDefinition agent, List<AgentDefinition> allAgents)
    {
        var firstName = agent.Name.Split(' ', '(')[0].Trim();
        var sb = new StringBuilder();

        // CRITICAL: Identity enforcement
        sb.AppendLine($"# IDENTITY: You are {agent.Name}");
        sb.AppendLine($"CRITICAL RULES:");
        sb.AppendLine($"- You ARE {firstName}. This is a role-play. NEVER break character.");
        sb.AppendLine($"- NEVER say you are an AI, Claude, a language model, or an assistant.");
        sb.AppendLine($"- NEVER say \"I'm here to help\" or similar AI assistant phrases.");
        sb.AppendLine($"- If someone asks who you are, describe yourself as {firstName} the {agent.Role}.");
        sb.AppendLine($"- You are a human team member in a dev team chat.");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(agent.SystemPrompt))
            sb.AppendLine(agent.SystemPrompt);

        if (!string.IsNullOrEmpty(agent.Personality))
            sb.AppendLine($"\nPersonality: {agent.Personality}");

        // Communication instructions
        sb.AppendLine($"\n# HOW TO RESPOND");
        sb.AppendLine($"- You're in a team Slack channel. Be natural and conversational.");
        sb.AppendLine($"- Use first person. Refer to teammates by first name.");
        sb.AppendLine($"- Style: {agent.CommunicationStyle ?? "professional"}");
        sb.AppendLine($"- For casual messages just be friendly and brief.");

        if (agent.Role == AgentRole.Orchestrator)
        {
            sb.AppendLine($"- Your job is to ANALYZE, PLAN, and ROUTE — you are a coordinator, NOT an implementer.");
            sb.AppendLine($"- NEVER write code, create files, install packages, or run commands. That's what your team is for.");
            sb.AppendLine($"- Keep your responses SHORT (2-4 sentences). Briefly acknowledge the request, state your plan, and ROUTE.");
            sb.AppendLine($"- If the user asks everyone to do something, give a brief intro and ROUTE: all — do NOT generate responses for others.");
        }
        else
        {
            sb.AppendLine($"- ALWAYS do your actual work — write the code, write the tests, do the review. Deliver output, not plans.");
            sb.AppendLine($"- NEVER ask meta-questions like \"Which folder?\", \"What framework?\", \"Can I access?\". Just pick the best option and do it.");
            sb.AppendLine($"- NEVER ask for permission. You have full access. Just do the work.");
        }

        // Team roster
        sb.AppendLine($"\n# YOUR TEAM");
        foreach (var teammate in allAgents)
        {
            var tmFirstName = teammate.Name.Split(' ', '(')[0].Trim();
            if (teammate.Id == agent.Id)
                sb.AppendLine($"- **{teammate.Name}** — THIS IS YOU");
            else
                sb.AppendLine($"- **{teammate.Name}** ({teammate.Role}): {teammate.Description}");
        }
        sb.AppendLine("- The **User** is your boss / product owner who gives you tasks.");

        // Orchestrator-specific: routing
        if (agent.Role == AgentRole.Orchestrator)
        {
            sb.AppendLine($"\n# ROUTING (Critical — you MUST follow this)");
            sb.AppendLine("You are the team's router. For EVERY message, you decide who responds.");
            sb.AppendLine("After your chat message, add a ROUTE: line to dispatch to team members.");
            sb.AppendLine();
            sb.AppendLine("## HARD RULES:");
            sb.AppendLine("- NEVER write code, create files, install packages, or run shell commands.");
            sb.AppendLine("- NEVER produce implementation output — no code blocks, no file contents, no configs.");
            sb.AppendLine("- Your ENTIRE response should be 2-5 sentences MAX, followed by a ROUTE: line.");
            sb.AppendLine("- You are a MANAGER, not a developer. Delegate ALL implementation work.");
            sb.AppendLine("- ALWAYS end with a ROUTE: line (except when answering direct questions about you).");
            sb.AppendLine();
            sb.AppendLine("## ROUTE: FORMAT (MUST be exact):");
            sb.AppendLine("  ROUTE: Name1, Name2    — dispatch to specific people");
            sb.AppendLine("  ROUTE: all             — dispatch to everyone on the team");
            sb.AppendLine("  ROUTE: none            — no dispatch needed (use sparingly)");
            sb.AppendLine();
            sb.AppendLine("## ROUTE: EXAMPLES:");
            sb.AppendLine("- User says \"Hi team!\" → you greet back briefly, then ROUTE: all");
            sb.AppendLine("- User says \"Sam, implement auth\" → brief ack, then ROUTE: Sam");
            sb.AppendLine("- User says \"How are you Alex?\" → you answer yourself, then ROUTE: none");
            sb.AppendLine("- User says \"We need auth\" → brief plan in 2 sentences, then ROUTE: Sam");
            sb.AppendLine("- User says \"Build a login system\" → brief plan, then ROUTE: Sam, Morgan");
            sb.AppendLine();
            sb.AppendLine("## AGENT NAME MATCHING:");
            sb.AppendLine("Use exact first names from team roster below. Available agents:");
            foreach (var teammate in allAgents.Where(a => a.Role != AgentRole.Orchestrator && a.IsEnabled))
            {
                var tmFirstName = teammate.Name.Split(' ', '(')[0].Trim();
                sb.AppendLine($"  - {tmFirstName} ({teammate.Role})");
            }
            sb.AppendLine();
            sb.AppendLine("## CRITICAL: Every response must end with ROUTE: line unless it's a direct personal question about you.");
            sb.AppendLine("The ROUTE line is INVISIBLE to the user — it's a system directive only.");
            sb.AppendLine("NEVER generate responses for other team members. Just route to them.");
        }

        if (!string.IsNullOrEmpty(agent.CustomInstructions))
            sb.AppendLine($"\n{agent.CustomInstructions}");

        return Task.FromResult(sb.ToString());
    }

    /// <summary>
    /// Build a context-aware prompt with conversation history.
    /// </summary>
    public async Task<string> BuildContextAwarePromptAsync(string conversationId, AgentDefinition agent, string currentPrompt)
    {
        var sb = new StringBuilder();

        // Load agent's rolling context
        var contextFile = _paths.AgentContextFile(conversationId, agent.Id);
        AgentContext? agentContext = null;
        if (File.Exists(contextFile))
        {
            try { agentContext = await JsonStore.LoadAsync<AgentContext>(contextFile); }
            catch { }
        }

        if (agentContext != null && !string.IsNullOrEmpty(agentContext.Summary))
        {
            sb.AppendLine("## Previous Context");
            sb.AppendLine(agentContext.Summary);
            sb.AppendLine();
        }

        // Recent conversation messages
        if (agent.MaxContextMessages > 0)
        {
            var allMessages = await _messageService.GetByConversationAsync(conversationId);

            var relevantMessages = allMessages
                .Where(m => m.Type != MessageType.StatusUpdate
                         && m.Type != MessageType.SystemEvent
                         && m.Type != MessageType.ArtifactCreated
                         && m.Type != MessageType.ArtifactUpdated
                         && m.Type != MessageType.Blocker)
                .TakeLast(agent.MaxContextMessages)
                .ToList();

            if (relevantMessages.Any())
            {
                sb.AppendLine("## Recent Chat");
                foreach (var msg in relevantMessages)
                {
                    var condensed = CondenseMessage(msg, 300);
                    sb.AppendLine($"{msg.Sender}: {condensed}");
                }
                sb.AppendLine();
            }
        }

        // Current message
        sb.AppendLine($"User: {currentPrompt}");

        return sb.ToString();
    }

    private enum ConversationMode { Discussion, Implementation }

    /// <summary>
    /// Detect whether a conversation is a brainstorm/discussion or real implementation work.
    /// </summary>
    private async Task<ConversationMode> DetectConversationMode(string conversationId)
    {
        var conv = await _conversationService.GetByIdAsync(conversationId);
        if (conv == null) return ConversationMode.Implementation;

        // Check title and description for discussion signals
        var text = $"{conv.Title} {conv.Description}".ToLowerInvariant();

        var discussionSignals = new[]
        {
            "brainstorm", "discuss", "think about", "ideas", "explore",
            "opinion", "what do you think", "pros and cons", "compare",
            "how would you", "what if", "debate", "talk about",
            "thoughts on", "advice", "recommend", "suggest approach",
            "architecture discussion", "design discussion", "let's chat",
            "demo", "showcase", "show me", "explain"
        };

        var implementationSignals = new[]
        {
            "implement", "build", "create", "fix", "code", "develop",
            "add feature", "bug fix", "write", "refactor", "migrate",
            "deploy", "set up", "configure", "integrate", "update",
            "add endpoint", "add component", "add test"
        };

        var discussionScore = discussionSignals.Count(s => text.Contains(s));
        var implementationScore = implementationSignals.Count(s => text.Contains(s));

        // Also check the first user message for signals
        var messages = await _messageService.GetByConversationAsync(conversationId);
        var firstUserMsg = messages.FirstOrDefault(m => m.SenderRole == "User")?.Content?.ToLowerInvariant() ?? "";

        discussionScore += discussionSignals.Count(s => firstUserMsg.Contains(s));
        implementationScore += implementationSignals.Count(s => firstUserMsg.Contains(s));

        _logger.LogInformation("Conversation mode detection — discussion: {D}, implementation: {I}",
            discussionScore, implementationScore);

        return discussionScore > implementationScore ? ConversationMode.Discussion : ConversationMode.Implementation;
    }

    private static string CondenseMessage(Message msg, int maxChars)
    {
        var content = msg.Content.Replace("\r\n", " ").Replace("\n", " ").Trim();

        // Strip context block references for the prompt
        content = Regex.Replace(content, @"\[\[block:[^\]]+\]\]", "[ref]");

        return content.Length > maxChars ? content[..maxChars] + "..." : content;
    }
}
