namespace AiDevTeam.Core.Contracts;

public class TestFailure
{
    public string TestName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public bool IsFlaky { get; set; }
}
