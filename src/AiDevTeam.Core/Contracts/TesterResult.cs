namespace AiDevTeam.Core.Contracts;

public class TesterResult
{
    public TestDecision Decision { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int TestsPassed { get; set; }
    public int TestsFailed { get; set; }
    public int TestsSkipped { get; set; }
    public List<TestFailure> Failures { get; set; } = [];
    public List<string> TestGaps { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public string? CoverageReport { get; set; }
}
