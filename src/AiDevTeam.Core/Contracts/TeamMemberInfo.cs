namespace AiDevTeam.Core.Contracts;

/// <summary>
/// Lightweight reference to a team member, included in task prompts
/// so the agent knows who else is on the team.
/// </summary>
public class TeamMemberInfo
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
