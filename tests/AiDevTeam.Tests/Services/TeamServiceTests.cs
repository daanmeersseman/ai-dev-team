using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;
using AiDevTeam.Infrastructure.Services;

namespace AiDevTeam.Tests.Services;

public class TeamServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StoragePaths _paths;
    private readonly TeamService _service;

    public TeamServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AiDevTeamTests_" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new StoragePaths(_tempDir);
        _service = new TeamService(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task CreateAsync_persists_team()
    {
        var team = new Team
        {
            Name = "Alpha",
            Description = "The alpha team",
            AgentIds = new List<string> { "agent-1", "agent-2" }
        };

        var created = await _service.CreateAsync(team);

        Assert.Equal("Alpha", created.Name);
        Assert.Equal("The alpha team", created.Description);
        Assert.Equal(2, created.AgentIds.Count);
    }

    [Fact]
    public async Task GetByIdAsync_returns_created_team()
    {
        var team = new Team { Name = "Bravo", Description = "desc" };
        await _service.CreateAsync(team);

        var loaded = await _service.GetByIdAsync(team.Id);

        Assert.NotNull(loaded);
        Assert.Equal(team.Id, loaded!.Id);
        Assert.Equal("Bravo", loaded.Name);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_missing()
    {
        var result = await _service.GetByIdAsync("nonexistent-id");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_returns_teams_ordered_by_name()
    {
        await _service.CreateAsync(new Team { Name = "Zulu" });
        await _service.CreateAsync(new Team { Name = "Alpha" });
        await _service.CreateAsync(new Team { Name = "Mike" });

        var all = await _service.GetAllAsync();

        Assert.Equal(3, all.Count);
        Assert.Equal("Alpha", all[0].Name);
        Assert.Equal("Mike", all[1].Name);
        Assert.Equal("Zulu", all[2].Name);
    }

    [Fact]
    public async Task GetAllAsync_returns_empty_when_none_exist()
    {
        var all = await _service.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task UpdateAsync_persists_changes()
    {
        var team = new Team { Name = "Original", Description = "old" };
        await _service.CreateAsync(team);

        team.Name = "Updated";
        team.Description = "new";
        await _service.UpdateAsync(team);

        var loaded = await _service.GetByIdAsync(team.Id);
        Assert.Equal("Updated", loaded!.Name);
        Assert.Equal("new", loaded.Description);
    }

    [Fact]
    public async Task DeleteAsync_removes_team()
    {
        var team = new Team { Name = "ToDelete" };
        await _service.CreateAsync(team);

        await _service.DeleteAsync(team.Id);

        Assert.Null(await _service.GetByIdAsync(team.Id));
    }

    [Fact]
    public async Task DeleteAsync_no_error_for_missing()
    {
        await _service.DeleteAsync("nonexistent");
    }
}
