namespace AiDevTeam.Core.Models;

public class AppSettings
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string DefaultCommunicationStyle { get; set; } = "professional";
    public TeamFlowConfiguration TeamFlow { get; set; } = new();
    public Dictionary<string, string> CustomSettings { get; set; } = [];
}
