namespace AiDevTeam.Core.Models;

public class Team
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> AgentIds { get; set; } = [];
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
