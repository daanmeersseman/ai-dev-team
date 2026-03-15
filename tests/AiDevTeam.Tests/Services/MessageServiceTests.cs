using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;
using AiDevTeam.Infrastructure.Services;

namespace AiDevTeam.Tests.Services;

public class MessageServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StoragePaths _paths;
    private readonly MessageService _service;

    public MessageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AiDevTeamTests_" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new StoragePaths(_tempDir);
        _service = new MessageService(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task AddAsync_with_request_persists_message()
    {
        var request = new AddMessageRequest
        {
            ConversationId = "conv-1",
            Sender = "Alice",
            SenderRole = "Coder",
            Type = MessageType.UserInstruction,
            Content = "Hello world"
        };

        var msg = await _service.AddAsync(request);

        Assert.NotNull(msg);
        Assert.Equal("conv-1", msg.ConversationId);
        Assert.Equal("Alice", msg.Sender);
        Assert.Equal("Coder", msg.SenderRole);
        Assert.Equal(MessageType.UserInstruction, msg.Type);
        Assert.Equal("Hello world", msg.Content);
    }

    [Fact]
    public async Task AddAsync_with_request_sets_optional_fields()
    {
        var request = new AddMessageRequest
        {
            ConversationId = "conv-1",
            Sender = "Bob",
            SenderRole = "Reviewer",
            Type = MessageType.Review,
            Content = "Looks good",
            RelatedArtifactId = "art-1",
            AgentRunId = "run-1",
            MetadataJson = "{\"key\":\"value\"}"
        };

        var msg = await _service.AddAsync(request);

        Assert.Equal("art-1", msg.RelatedArtifactId);
        Assert.Equal("run-1", msg.AgentRunId);
        Assert.Equal("{\"key\":\"value\"}", msg.MetadataJson);
    }

#pragma warning disable CS0618
    [Fact]
    public async Task AddAsync_legacy_overload_persists_message()
    {
        var msg = await _service.AddAsync(
            "conv-2", "Charlie", "Tester",
            MessageType.TestResult, "All tests pass");

        Assert.Equal("conv-2", msg.ConversationId);
        Assert.Equal("Charlie", msg.Sender);
        Assert.Equal("All tests pass", msg.Content);
    }

    [Fact]
    public async Task AddAsync_legacy_overload_with_optional_params()
    {
        var msg = await _service.AddAsync(
            "conv-2", "Charlie", "Tester",
            MessageType.TestResult, "Pass",
            relatedArtifactId: "art-2",
            agentRunId: "run-2",
            metadataJson: "{\"x\":1}");

        Assert.Equal("art-2", msg.RelatedArtifactId);
        Assert.Equal("run-2", msg.AgentRunId);
        Assert.Equal("{\"x\":1}", msg.MetadataJson);
    }
#pragma warning restore CS0618

    [Fact]
    public async Task GetByConversationAsync_returns_messages_ordered_by_time()
    {
        var request1 = new AddMessageRequest
        {
            ConversationId = "conv-3",
            Sender = "A",
            SenderRole = "Coder",
            Type = MessageType.UserInstruction,
            Content = "First"
        };
        await _service.AddAsync(request1);
        await Task.Delay(50);

        var request2 = new AddMessageRequest
        {
            ConversationId = "conv-3",
            Sender = "B",
            SenderRole = "Reviewer",
            Type = MessageType.Review,
            Content = "Second"
        };
        await _service.AddAsync(request2);

        var messages = await _service.GetByConversationAsync("conv-3");

        Assert.Equal(2, messages.Count);
        Assert.Equal("First", messages[0].Content);
        Assert.Equal("Second", messages[1].Content);
        Assert.True(messages[0].CreatedAt <= messages[1].CreatedAt);
    }

    [Fact]
    public async Task GetByConversationAsync_returns_empty_for_missing_conversation()
    {
        var messages = await _service.GetByConversationAsync("nonexistent");
        Assert.Empty(messages);
    }
}
