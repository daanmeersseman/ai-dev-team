namespace AiDevTeam.Core.Models;

/// <summary>
/// Rolling context maintained per agent per conversation to minimize token usage.
/// Instead of sending full chat history, each agent maintains a condensed summary.
/// </summary>
public class AgentContext
{
    public string AgentId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> KeyDecisions { get; set; } = [];
    public int RunCount { get; set; }
    public int EstimatedTokensUsed { get; set; }
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}
