using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;
using AiDevTeam.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace AiDevTeam.Tests.Services;

public class AgentResponseProcessorTests : IDisposable
{
    private readonly IMessageService _messageService;
    private readonly IArtifactService _artifactService;
    private readonly IContextBlockService _contextBlockService;
    private readonly StoragePaths _paths;
    private readonly ILogger<AgentResponseProcessor> _logger;
    private readonly AgentResponseProcessor _sut;
    private readonly string _tempDir;

    public AgentResponseProcessorTests()
    {
        _messageService = Substitute.For<IMessageService>();
        _artifactService = Substitute.For<IArtifactService>();
        _contextBlockService = Substitute.For<IContextBlockService>();
        _logger = Substitute.For<ILogger<AgentResponseProcessor>>();

        _tempDir = Path.Combine(Path.GetTempPath(), "arp-tests-" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new StoragePaths(_tempDir);

        // Default: AddAsync returns a dummy message
#pragma warning disable CS0618 // Obsolete
        _messageService.AddAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<MessageType>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(ci => new Message
            {
                ConversationId = ci.ArgAt<string>(0),
                Sender = ci.ArgAt<string>(1),
                Content = ci.ArgAt<string>(4)
            });
#pragma warning restore CS0618

        // Default: CreateAsync returns a dummy artifact
        _artifactService.CreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ArtifactType>(),
            Arg.Any<string>(), Arg.Any<string?>())
            .Returns(ci => new Artifact
            {
                ConversationId = ci.ArgAt<string>(0),
                FileName = ci.ArgAt<string>(1)
            });

        _sut = new AgentResponseProcessor(_messageService, _artifactService, _contextBlockService, _paths, _logger);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best effort cleanup */ }
    }

    private static AgentDefinition MakeCoder() => new()
    {
        Id = "coder-1",
        Name = "Sam (Coder)",
        Role = AgentRole.Coder,
        MaxContextTokens = 2000
    };

    private static AgentDefinition MakeOrchestrator() => new()
    {
        Id = "orch-1",
        Name = "Alex (Tech Lead)",
        Role = AgentRole.Orchestrator,
        MaxContextTokens = 2000
    };

    // ── ProcessAgentResponseAsync ────────────────────────────────────

    [Fact]
    public async Task ProcessAgentResponseAsync_short_response_posts_directly_to_chat()
    {
        var agent = MakeCoder();
        var shortResponse = "Looks good, no changes needed.";

        await _sut.ProcessAgentResponseAsync("conv-1", agent, shortResponse, "run-1");

#pragma warning disable CS0618
        await _messageService.Received(1).AddAsync(
            "conv-1", "Sam (Coder)", "Coder",
            MessageType.AgentThoughtSummary, shortResponse,
            agentRunId: "run-1");
#pragma warning restore CS0618
    }

    [Fact]
    public async Task ProcessAgentResponseAsync_long_technical_response_creates_artifact_and_posts_full_content()
    {
        var agent = MakeCoder();
        var longResponse = "I've completed the implementation.\n\n" +
            "## Changes Made\n\n" +
            "```csharp\npublic class AuthService\n{\n    // implementation with more than 20 chars of code here to pass the length check\n}\n```\n" +
            new string('.', 300);

        await _sut.ProcessAgentResponseAsync("conv-1", agent, longResponse, "run-1");

        // Should create the main markdown artifact for reference
        await _artifactService.Received().CreateAsync(
            "conv-1",
            "sam-implementation.md",
            ArtifactType.Markdown,
            longResponse,
            "Sam (Coder)");

        // Should post the full response to chat (not just a brief summary)
#pragma warning disable CS0618
        await _messageService.Received().AddAsync(
            "conv-1", "Sam (Coder)", "Coder",
            MessageType.AgentThoughtSummary,
            Arg.Is<string>(s => s.Contains("I've completed the implementation")),
            Arg.Any<string?>(), Arg.Is("run-1"), Arg.Any<string?>());
#pragma warning restore CS0618
    }

    // ── ExtractAndSaveCodeArtifactsAsync ─────────────────────────────

    [Fact]
    public async Task ExtractAndSaveCodeArtifactsAsync_extracts_code_blocks_from_response()
    {
        var agent = MakeCoder();
        var response =
            "Here is the implementation:\n\n" +
            "```csharp\npublic class UserService\n{\n    public void CreateUser() { /* real implementation here */ }\n}\n```\n\n" +
            "And the interface:\n\n" +
            "```csharp\npublic interface IUserService\n{\n    void CreateUser(); // interface definition here\n}\n```";

        await _sut.ExtractAndSaveCodeArtifactsAsync("conv-1", agent, response, "run-1");

        // Should extract 2 code artifacts
        await _artifactService.Received(2).CreateAsync(
            "conv-1",
            Arg.Is<string>(f => f.EndsWith(".cs")),
            ArtifactType.Code,
            Arg.Any<string>(),
            "Sam (Coder)");
    }

    [Fact]
    public async Task ExtractAndSaveCodeArtifactsAsync_no_code_blocks_produces_no_artifacts()
    {
        var agent = MakeCoder();
        var response = "Everything looks good. No code changes required.";

        await _sut.ExtractAndSaveCodeArtifactsAsync("conv-1", agent, response, "run-1");

        await _artifactService.DidNotReceive().CreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ArtifactType>(),
            Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task ExtractAndSaveCodeArtifactsAsync_skips_tiny_code_snippets()
    {
        var agent = MakeCoder();
        // Code block with < 20 characters of content
        var response = "Example:\n\n```csharp\nvar x = 1;\n```";

        await _sut.ExtractAndSaveCodeArtifactsAsync("conv-1", agent, response, "run-1");

        await _artifactService.DidNotReceive().CreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ArtifactType>(),
            Arg.Any<string>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task ExtractAndSaveCodeArtifactsAsync_maps_language_to_correct_extension()
    {
        var agent = MakeCoder();
        var response =
            "Python script:\n\n" +
            "```python\ndef hello_world():\n    print('Hello, World! This is a sufficiently long snippet')\n```\n\n" +
            "SQL query:\n\n" +
            "```sql\nSELECT * FROM users WHERE active = 1 AND created_at > '2024-01-01'\n```";

        await _sut.ExtractAndSaveCodeArtifactsAsync("conv-1", agent, response, "run-1");

        await _artifactService.Received().CreateAsync(
            "conv-1",
            Arg.Is<string>(f => f.EndsWith(".py")),
            ArtifactType.Code,
            Arg.Any<string>(),
            "Sam (Coder)");

        await _artifactService.Received().CreateAsync(
            "conv-1",
            Arg.Is<string>(f => f.EndsWith(".sql")),
            ArtifactType.Code,
            Arg.Any<string>(),
            "Sam (Coder)");
    }

    // ── UpdateAgentContextAsync ──────────────────────────────────────

    [Fact]
    public async Task UpdateAgentContextAsync_creates_context_file_for_new_agent()
    {
        var agent = MakeCoder();
        var convId = "conv-ctx-1";

        await _sut.UpdateAgentContextAsync(convId, agent, "implement auth", "Done implementing auth service.", true);

        var contextFile = _paths.AgentContextFile(convId, agent.Id);
        Assert.True(File.Exists(contextFile), "Context file should be created");

        var context = await JsonStore.LoadAsync<AgentContext>(contextFile);
        Assert.Equal(1, context.RunCount);
        Assert.Equal(agent.Id, context.AgentId);
        Assert.Equal(convId, context.ConversationId);
        Assert.Contains("Run #1", context.Summary);
    }

    [Fact]
    public async Task UpdateAgentContextAsync_increments_run_count_on_subsequent_calls()
    {
        var agent = MakeCoder();
        var convId = "conv-ctx-2";

        await _sut.UpdateAgentContextAsync(convId, agent, "prompt 1", "response 1", true);
        await _sut.UpdateAgentContextAsync(convId, agent, "prompt 2", "response 2", true);

        var context = await JsonStore.LoadAsync<AgentContext>(_paths.AgentContextFile(convId, agent.Id));
        Assert.Equal(2, context.RunCount);
        Assert.Contains("Run #1", context.Summary);
        Assert.Contains("Run #2", context.Summary);
    }

    [Fact]
    public async Task UpdateAgentContextAsync_marks_failure_in_summary()
    {
        var agent = MakeCoder();
        var convId = "conv-ctx-3";

        await _sut.UpdateAgentContextAsync(convId, agent, "do work", "Error: something broke", false);

        var context = await JsonStore.LoadAsync<AgentContext>(_paths.AgentContextFile(convId, agent.Id));
        Assert.Contains("FAILED", context.Summary);
    }

    [Fact]
    public async Task UpdateAgentContextAsync_extracts_key_decisions_from_bullet_points()
    {
        var agent = MakeCoder();
        var convId = "conv-ctx-4";
        var response = "Here is what I did:\n" +
            "- Implemented the authentication middleware using JWT tokens\n" +
            "- Added rate limiting to protect against brute force attacks\n" +
            "- Created unit tests for all new endpoints";

        await _sut.UpdateAgentContextAsync(convId, agent, "implement auth", response, true);

        var context = await JsonStore.LoadAsync<AgentContext>(_paths.AgentContextFile(convId, agent.Id));
        Assert.NotEmpty(context.KeyDecisions);
        Assert.True(context.KeyDecisions.Count <= 3);
    }

    [Fact]
    public async Task UpdateAgentContextAsync_accumulates_estimated_tokens()
    {
        var agent = MakeCoder();
        var convId = "conv-ctx-5";

        await _sut.UpdateAgentContextAsync(convId, agent, "prompt one", "response one", true);
        var ctx1 = await JsonStore.LoadAsync<AgentContext>(_paths.AgentContextFile(convId, agent.Id));
        var firstTokens = ctx1.EstimatedTokensUsed;
        Assert.True(firstTokens > 0);

        await _sut.UpdateAgentContextAsync(convId, agent, "prompt two", "response two", true);
        var ctx2 = await JsonStore.LoadAsync<AgentContext>(_paths.AgentContextFile(convId, agent.Id));
        Assert.True(ctx2.EstimatedTokensUsed > firstTokens);
    }
}
