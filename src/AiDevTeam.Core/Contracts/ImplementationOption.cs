namespace AiDevTeam.Core.Contracts;

public class ImplementationOption
{
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Pros { get; set; }
    public string? Cons { get; set; }
    public bool IsRecommended { get; set; }
}
