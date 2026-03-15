using AiDevTeam.Infrastructure.FileStore;
using System.Diagnostics;

namespace AiDevTeam.Tests.FileStore;

public class JsonStoreTests : IDisposable
{
    private readonly string _tempDir;

    public JsonStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "jsonstore-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best effort cleanup */ }
    }

    private string TempFile(string name = "test.json") => Path.Combine(_tempDir, name);

    // ── Simple DTO for testing ───────────────────────────────────────

    public class TestItem
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    // ── LoadAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_returns_new_T_for_missing_file()
    {
        var result = await JsonStore.LoadAsync<TestItem>(TempFile("nonexistent.json"));
        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Name);
        Assert.Equal(0, result.Value);
    }

    // ── SaveAsync + LoadAsync roundtrip ──────────────────────────────

    [Fact]
    public async Task SaveAsync_and_LoadAsync_roundtrip()
    {
        var path = TempFile("roundtrip.json");
        var original = new TestItem { Name = "test-item", Value = 42 };

        await JsonStore.SaveAsync(path, original);
        var loaded = await JsonStore.LoadAsync<TestItem>(path);

        Assert.Equal("test-item", loaded.Name);
        Assert.Equal(42, loaded.Value);
    }

    [Fact]
    public async Task SaveAsync_creates_parent_directory_if_needed()
    {
        var path = Path.Combine(_tempDir, "sub", "deep", "item.json");
        var item = new TestItem { Name = "nested", Value = 7 };

        await JsonStore.SaveAsync(path, item);

        Assert.True(File.Exists(path));
        var loaded = await JsonStore.LoadAsync<TestItem>(path);
        Assert.Equal("nested", loaded.Name);
    }

    // ── LoadListAsync ────────────────────────────────────────────────

    [Fact]
    public async Task LoadListAsync_returns_empty_for_missing_file()
    {
        var result = await JsonStore.LoadListAsync<TestItem>(TempFile("no-list.json"));
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // ── SaveListAsync + LoadListAsync roundtrip ──────────────────────

    [Fact]
    public async Task SaveListAsync_and_LoadListAsync_roundtrip()
    {
        var path = TempFile("list-roundtrip.json");
        var items = new List<TestItem>
        {
            new() { Name = "alpha", Value = 1 },
            new() { Name = "beta", Value = 2 },
            new() { Name = "gamma", Value = 3 }
        };

        await JsonStore.SaveListAsync(path, items);
        var loaded = await JsonStore.LoadListAsync<TestItem>(path);

        Assert.Equal(3, loaded.Count);
        Assert.Equal("alpha", loaded[0].Name);
        Assert.Equal("beta", loaded[1].Name);
        Assert.Equal("gamma", loaded[2].Name);
    }

    // ── UpdateListAsync ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateListAsync_adds_to_existing_list()
    {
        var path = TempFile("update-list.json");

        // Seed with initial data
        var initial = new List<TestItem> { new() { Name = "first", Value = 1 } };
        await JsonStore.SaveListAsync(path, initial);

        // Update: append a new item
        await JsonStore.UpdateListAsync<TestItem>(path, list =>
        {
            list.Add(new TestItem { Name = "second", Value = 2 });
            return list;
        });

        var loaded = await JsonStore.LoadListAsync<TestItem>(path);
        Assert.Equal(2, loaded.Count);
        Assert.Equal("first", loaded[0].Name);
        Assert.Equal("second", loaded[1].Name);
    }

    [Fact]
    public async Task UpdateListAsync_creates_file_if_missing()
    {
        var path = TempFile("update-new.json");

        await JsonStore.UpdateListAsync<TestItem>(path, list =>
        {
            list.Add(new TestItem { Name = "created", Value = 99 });
            return list;
        });

        var loaded = await JsonStore.LoadListAsync<TestItem>(path);
        Assert.Single(loaded);
        Assert.Equal("created", loaded[0].Name);
    }

    [Fact]
    public async Task UpdateListAsync_can_transform_existing_items()
    {
        var path = TempFile("update-transform.json");
        await JsonStore.SaveListAsync(path, new List<TestItem>
        {
            new() { Name = "item", Value = 10 }
        });

        await JsonStore.UpdateListAsync<TestItem>(path, list =>
        {
            foreach (var item in list) item.Value *= 2;
            return list;
        });

        var loaded = await JsonStore.LoadListAsync<TestItem>(path);
        Assert.Equal(20, loaded[0].Value);
    }

    // ── Concurrent writes to different files ─────────────────────────

    [Fact]
    public async Task Concurrent_writes_to_different_files_do_not_block_each_other()
    {
        const int fileCount = 5;
        var tasks = new List<Task>();
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < fileCount; i++)
        {
            var path = TempFile($"concurrent-{i}.json");
            var item = new TestItem { Name = $"item-{i}", Value = i };
            tasks.Add(JsonStore.SaveAsync(path, item));
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        // All files should exist and be valid
        for (var i = 0; i < fileCount; i++)
        {
            var loaded = await JsonStore.LoadAsync<TestItem>(TempFile($"concurrent-{i}.json"));
            Assert.Equal($"item-{i}", loaded.Name);
            Assert.Equal(i, loaded.Value);
        }

        // Sanity check: parallel writes should complete quickly (not serialized)
        // This is a loose bound -- just ensuring they didn't queue up sequentially
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Concurrent writes took {sw.ElapsedMilliseconds}ms, expected < 5000ms");
    }

    // ── AtomicWrite: .tmp file cleanup ───────────────────────────────

    [Fact]
    public async Task AtomicWrite_cleans_up_tmp_file()
    {
        var path = TempFile("atomic.json");
        var tmpPath = path + ".tmp";

        await JsonStore.SaveAsync(path, new TestItem { Name = "atomic", Value = 1 });

        Assert.True(File.Exists(path), "Target file should exist after save");
        Assert.False(File.Exists(tmpPath), ".tmp file should be cleaned up after atomic write");
    }

    [Fact]
    public async Task SaveAsync_overwrites_existing_file()
    {
        var path = TempFile("overwrite.json");

        await JsonStore.SaveAsync(path, new TestItem { Name = "original", Value = 1 });
        await JsonStore.SaveAsync(path, new TestItem { Name = "updated", Value = 2 });

        var loaded = await JsonStore.LoadAsync<TestItem>(path);
        Assert.Equal("updated", loaded.Name);
        Assert.Equal(2, loaded.Value);
    }
}
