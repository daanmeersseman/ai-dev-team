using AiDevTeam.Core.Interfaces;
using AiDevTeam.Providers;

namespace AiDevTeam.Tests.Providers;

public class MockProviderTests
{
    private readonly MockProvider _sut = new();

    [Fact]
    public void ProviderType_ReturnsMock()
    {
        Assert.Equal("Mock", _sut.ProviderType);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNonEmptyResult()
    {
        var request = new AgentRunRequest
        {
            Prompt = "Build a REST API",
            SystemPrompt = "You are a helpful agent."
        };

        var result = await _sut.ExecuteAsync(request);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Output);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.DurationMs > 0);
    }

    [Theory]
    [InlineData("You are an orchestrator agent.", "Task Analysis")]
    [InlineData("You are a reviewer agent.", "Code Review")]
    [InlineData("You are a coder agent.", "Implementation Complete")]
    [InlineData("You are a tester agent.", "Test Results")]
    [InlineData("You are a database specialist.", "Database Impact Analysis")]
    public async Task ExecuteAsync_ReturnsRoleAppropriateContent(string systemPrompt, string expectedFragment)
    {
        var request = new AgentRunRequest
        {
            Prompt = "Do something useful",
            SystemPrompt = systemPrompt
        };

        var result = await _sut.ExecuteAsync(request);

        Assert.Contains(expectedFragment, result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_GeneratesArtifactsForCoderRole()
    {
        var request = new AgentRunRequest
        {
            Prompt = "Implement the login feature",
            SystemPrompt = "You are a senior coder on the team."
        };

        var result = await _sut.ExecuteAsync(request);

        Assert.NotEmpty(result.Artifacts);
        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("implementation-notes.md", artifact.FileName);
        Assert.NotEmpty(artifact.Content);
        Assert.Contains("Implement the login feature", artifact.Content);
    }

    [Fact]
    public async Task ExecuteAsync_NoArtifactsForNonCoderRoles()
    {
        var request = new AgentRunRequest
        {
            Prompt = "Review the code",
            SystemPrompt = "You are a reviewer."
        };

        var result = await _sut.ExecuteAsync(request);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultRole_WhenSystemPromptNull()
    {
        var request = new AgentRunRequest
        {
            Prompt = "Hello world",
            SystemPrompt = null
        };

        var result = await _sut.ExecuteAsync(request);

        Assert.True(result.Success);
        Assert.Contains("Agent Response", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_InvokesOnOutputReceived()
    {
        var messages = new List<string>();
        var request = new AgentRunRequest
        {
            Prompt = "Test output callbacks",
            SystemPrompt = "You are an agent.",
            OnOutputReceived = msg => messages.Add(msg)
        };

        await _sut.ExecuteAsync(request);

        Assert.Contains("Processing prompt...", messages);
        Assert.Contains("Analyzing requirements...", messages);
        Assert.Contains("Generating response...", messages);
    }

    [Fact]
    public async Task ExecuteAsync_SupportsCancellation()
    {
        using var cts = new CancellationTokenSource();
        var request = new AgentRunRequest
        {
            Prompt = "This should be cancelled",
            SystemPrompt = "You are an agent."
        };

        // Cancel immediately
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.ExecuteAsync(request, cts.Token));
    }
}
