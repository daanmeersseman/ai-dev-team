using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;
using AiDevTeam.Infrastructure.Services;

namespace AiDevTeam.Tests.Services;

public class AgentDefinitionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StoragePaths _paths;
    private readonly AgentDefinitionService _service;

    public AgentDefinitionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AiDevTeamTests_" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new StoragePaths(_tempDir);
        _service = new AgentDefinitionService(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task CreateAsync_persists_agent()
    {
        var agent = new AgentDefinition
        {
            Name = "Sam",
            Role = AgentRole.Coder,
            Description = "A senior coder",
            SystemPrompt = "You are a coder."
        };

        var created = await _service.CreateAsync(agent);

        Assert.Equal("Sam", created.Name);
        Assert.Equal(AgentRole.Coder, created.Role);
    }

    [Fact]
    public async Task GetByIdAsync_returns_created_agent()
    {
        var agent = new AgentDefinition { Name = "Alice", Role = AgentRole.Reviewer };
        await _service.CreateAsync(agent);

        var loaded = await _service.GetByIdAsync(agent.Id);

        Assert.NotNull(loaded);
        Assert.Equal(agent.Id, loaded!.Id);
        Assert.Equal("Alice", loaded.Name);
        Assert.Equal(AgentRole.Reviewer, loaded.Role);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_missing()
    {
        var result = await _service.GetByIdAsync("nonexistent-id");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_returns_agents_ordered_by_role_then_name()
    {
        await _service.CreateAsync(new AgentDefinition { Name = "Zoe", Role = AgentRole.Coder });
        await _service.CreateAsync(new AgentDefinition { Name = "Alice", Role = AgentRole.Coder });
        await _service.CreateAsync(new AgentDefinition { Name = "Bob", Role = AgentRole.Reviewer });

        var all = await _service.GetAllAsync();

        Assert.Equal(3, all.Count);
        // Reviewer (enum 1) sorts before Coder (enum 2)
        Assert.Equal("Bob", all[0].Name);
        Assert.Equal("Alice", all[1].Name);
        Assert.Equal("Zoe", all[2].Name);
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
        var agent = new AgentDefinition { Name = "Original", Role = AgentRole.Tester };
        await _service.CreateAsync(agent);

        agent.Name = "Updated";
        agent.Role = AgentRole.Coder;
        await _service.UpdateAsync(agent);

        var loaded = await _service.GetByIdAsync(agent.Id);
        Assert.Equal("Updated", loaded!.Name);
        Assert.Equal(AgentRole.Coder, loaded.Role);
    }

    [Fact]
    public async Task DeleteAsync_removes_agent()
    {
        var agent = new AgentDefinition { Name = "ToDelete", Role = AgentRole.Custom };
        await _service.CreateAsync(agent);

        await _service.DeleteAsync(agent.Id);

        Assert.Null(await _service.GetByIdAsync(agent.Id));
    }

    [Fact]
    public async Task DeleteAsync_no_error_for_missing()
    {
        await _service.DeleteAsync("nonexistent");
    }
}
