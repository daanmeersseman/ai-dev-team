using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;
using AiDevTeam.Infrastructure.Services;

namespace AiDevTeam.Tests.Services;

public class ArtifactServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StoragePaths _paths;
    private readonly ArtifactService _service;

    public ArtifactServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AiDevTeamTests_" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new StoragePaths(_tempDir);
        _service = new ArtifactService(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task CreateAsync_persists_artifact_and_file()
    {
        var artifact = await _service.CreateAsync("conv-1", "hello.cs", ArtifactType.Code, "class Hello {}", "Sam");

        Assert.NotNull(artifact);
        Assert.Equal("conv-1", artifact.ConversationId);
        Assert.Equal("hello.cs", artifact.FileName);
        Assert.Equal("hello", artifact.DisplayName);
        Assert.Equal(ArtifactType.Code, artifact.Type);
        Assert.Equal("Sam", artifact.CreatedByAgent);
        Assert.Equal("Sam", artifact.LastModifiedByAgent);
        Assert.True(artifact.FileSizeBytes > 0);
    }

    [Fact]
    public async Task GetByConversationAsync_returns_artifacts_ordered_by_time()
    {
        await _service.CreateAsync("conv-2", "first.txt", ArtifactType.Text, "aaa");
        await Task.Delay(50);
        await _service.CreateAsync("conv-2", "second.txt", ArtifactType.Text, "bbb");

        var artifacts = await _service.GetByConversationAsync("conv-2");

        Assert.Equal(2, artifacts.Count);
        Assert.Equal("first.txt", artifacts[0].FileName);
        Assert.Equal("second.txt", artifacts[1].FileName);
        Assert.True(artifacts[0].CreatedAt <= artifacts[1].CreatedAt);
    }

    [Fact]
    public async Task GetByConversationAsync_returns_empty_for_missing()
    {
        var artifacts = await _service.GetByConversationAsync("nonexistent");
        Assert.Empty(artifacts);
    }

    [Fact]
    public async Task UpdateContentAsync_updates_file_and_metadata()
    {
        var artifact = await _service.CreateAsync("conv-3", "doc.md", ArtifactType.Markdown, "# Old");

        var updated = await _service.UpdateContentAsync(artifact.Id, "conv-3", "# New Content", "Reviewer");

        Assert.Equal("Reviewer", updated.LastModifiedByAgent);
        Assert.True(updated.FileSizeBytes > 0);

        var content = await _service.GetContentAsync("conv-3", "doc.md");
        Assert.Equal("# New Content", content);
    }

    [Fact]
    public async Task GetContentAsync_returns_file_content()
    {
        await _service.CreateAsync("conv-4", "data.json", ArtifactType.Json, "{\"key\":\"value\"}");

        var content = await _service.GetContentAsync("conv-4", "data.json");

        Assert.Equal("{\"key\":\"value\"}", content);
    }

    [Fact]
    public async Task GetContentAsync_returns_empty_for_missing_file()
    {
        var content = await _service.GetContentAsync("conv-5", "missing.txt");
        Assert.Equal(string.Empty, content);
    }

    [Fact]
    public void GetArtifactDirectory_creates_and_returns_directory()
    {
        var dir = _service.GetArtifactDirectory("conv-6");

        Assert.True(Directory.Exists(dir));
        Assert.Contains("conv-6", dir);
    }
}
