using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiDevTeam.Core.Contracts;

/// <summary>
/// Structured output from the orchestrator when analyzing a task.
/// </summary>
public class OrchestratorResult
{
    public OrchestratorAction Action { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<ImplementationOption> Options { get; set; } = [];
    public List<string> Questions { get; set; } = [];

    /// <summary>
    /// Plan can be a string (markdown) or an object in the LLM response.
    /// The converter handles both: objects are serialized to a JSON string.
    /// </summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Plan { get; set; }

    public List<PlannedStep> PlannedSteps { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public string? EstimatedComplexity { get; set; }

    /// <summary>
    /// Natural language message for the chat, addressing the user directly.
    /// This is what the user sees in the conversation.
    /// </summary>
    public string? ChatMessage { get; set; }
}

/// <summary>
/// Accepts both JSON strings and objects/arrays. When the value is an object or array,
/// it is serialized back to a JSON string so the property always holds a string.
/// </summary>
public class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            // For objects/arrays, read the entire element and convert to string
            JsonTokenType.StartObject or JsonTokenType.StartArray =>
                JsonSerializer.Serialize(JsonDocument.ParseValue(ref reader).RootElement),
            JsonTokenType.Number => reader.GetDecimal().ToString(),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => null
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

public enum OrchestratorAction
{
    ProposePlan,
    PresentOptions,
    AskQuestions,
    ProceedDirectly,
    /// <summary>
    /// Used when the user sends a casual chat message (e.g. a question) that
    /// doesn't require workflow progression. The chatMessage is posted and the
    /// workflow stays in its current state (typically WaitingForInput).
    /// </summary>
    ChatResponse
}
