using AiDevTeam.Core.Contracts;

namespace AiDevTeam.Core.Interfaces;

/// <summary>
/// Parses agent freeform text responses into structured contracts.
/// Handles JSON extraction, validation, and fallback.
/// </summary>
public interface IOutputParser
{
    ParseResult<OrchestratorResult> ParseOrchestratorOutput(string rawOutput);
    ParseResult<CoderResult> ParseCoderOutput(string rawOutput);
    ParseResult<ReviewResult> ParseReviewerOutput(string rawOutput);
    ParseResult<TesterResult> ParseTesterOutput(string rawOutput);
}

public class ParseResult<T> where T : class
{
    public bool Success { get; set; }
    public T? Value { get; set; }
    public string? RawOutput { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FallbackSummary { get; set; }

    public static ParseResult<T> Ok(T value, string rawOutput) => new()
        { Success = true, Value = value, RawOutput = rawOutput };

    public static ParseResult<T> Fail(string error, string rawOutput, string? fallbackSummary = null) => new()
        { Success = false, ErrorMessage = error, RawOutput = rawOutput, FallbackSummary = fallbackSummary };
}
