using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;
using AiDevTeam.Infrastructure.Services;

namespace AiDevTeam.Tests.Services;

public class ConversationServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StoragePaths _paths;
    private readonly ConversationService _service;

    public ConversationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AiDevTeamTests_" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new StoragePaths(_tempDir);
        _service = new ConversationService(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task CreateAsync_persists_conversation()
    {
        var conv = await _service.CreateAsync("Test Title", "A description", Priority.High, "tag1", "team-1");

        Assert.NotNull(conv);
        Assert.Equal("Test Title", conv.Title);
        Assert.Equal("A description", conv.Description);
        Assert.Equal(Priority.High, conv.Priority);
        Assert.Equal("tag1", conv.Tags);
        Assert.Equal("team-1", conv.TeamId);
        Assert.Equal(ConversationStatus.New, conv.Status);
    }

    [Fact]
    public async Task GetByIdAsync_returns_created_conversation()
    {
        var created = await _service.CreateAsync("Roundtrip", "desc");

        var loaded = await _service.GetByIdAsync(created.Id);

        Assert.NotNull(loaded);
        Assert.Equal(created.Id, loaded!.Id);
        Assert.Equal("Roundtrip", loaded.Title);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_missing()
    {
        var result = await _service.GetByIdAsync("nonexistent-id");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_returns_all_ordered_by_updated_descending()
    {
        var first = await _service.CreateAsync("First", "desc");
        await Task.Delay(50);
        var second = await _service.CreateAsync("Second", "desc");

        var all = await _service.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal("Second", all[0].Title);
        Assert.Equal("First", all[1].Title);
    }

    [Fact]
    public async Task GetAllAsync_returns_empty_when_no_conversations()
    {
        var all = await _service.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task UpdateStatusAsync_changes_status()
    {
        var conv = await _service.CreateAsync("Status Test", "desc");

        await _service.UpdateStatusAsync(conv.Id, ConversationStatus.InProgress);

        var loaded = await _service.GetByIdAsync(conv.Id);
        Assert.Equal(ConversationStatus.InProgress, loaded!.Status);
    }

    [Fact]
    public async Task UpdateAsync_persists_changes()
    {
        var conv = await _service.CreateAsync("Original", "desc");
        conv.Title = "Updated";

        await _service.UpdateAsync(conv);

        var loaded = await _service.GetByIdAsync(conv.Id);
        Assert.Equal("Updated", loaded!.Title);
    }

    [Fact]
    public async Task DeleteAsync_removes_conversation()
    {
        var conv = await _service.CreateAsync("To Delete", "desc");

        await _service.DeleteAsync(conv.Id);

        Assert.Null(await _service.GetByIdAsync(conv.Id));
    }

    [Fact]
    public async Task DeleteAsync_no_error_for_missing()
    {
        await _service.DeleteAsync("nonexistent");
    }
}
