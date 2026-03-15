namespace AiDevTeam.Core.Contracts;

public class ChangedFile
{
    public string FilePath { get; set; } = string.Empty;
    public FileChangeType ChangeType { get; set; }
    public string? Description { get; set; }
}

public enum FileChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed
}
