using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiDevTeam.Infrastructure.FileStore;

public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private static SemaphoreSlim GetLock(string path)
        => _locks.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));

    public static async Task<T> LoadAsync<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        var fileLock = GetLock(path);
        await fileLock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
        }
        finally { fileLock.Release(); }
    }

    public static async Task SaveAsync<T>(string path, T data)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);
        var fileLock = GetLock(path);
        await fileLock.WaitAsync();
        try
        {
            await AtomicWriteAsync(path, JsonSerializer.Serialize(data, Options));
        }
        finally { fileLock.Release(); }
    }

    public static async Task<List<T>> LoadListAsync<T>(string path)
    {
        if (!File.Exists(path)) return new List<T>();
        var fileLock = GetLock(path);
        await fileLock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>();
        }
        finally { fileLock.Release(); }
    }

    public static async Task SaveListAsync<T>(string path, List<T> data)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);
        var fileLock = GetLock(path);
        await fileLock.WaitAsync();
        try
        {
            await AtomicWriteAsync(path, JsonSerializer.Serialize(data, Options));
        }
        finally { fileLock.Release(); }
    }

    /// <summary>
    /// Atomic read-modify-write for list files. Holds lock across the full cycle.
    /// </summary>
    public static async Task UpdateListAsync<T>(string path, Func<List<T>, List<T>> updater)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);
        var fileLock = GetLock(path);
        await fileLock.WaitAsync();
        try
        {
            var list = File.Exists(path)
                ? JsonSerializer.Deserialize<List<T>>(await File.ReadAllTextAsync(path), Options) ?? new List<T>()
                : new List<T>();
            list = updater(list);
            await AtomicWriteAsync(path, JsonSerializer.Serialize(list, Options));
        }
        finally { fileLock.Release(); }
    }

    private static async Task AtomicWriteAsync(string path, string content)
    {
        var tmpPath = path + ".tmp";
        await File.WriteAllTextAsync(tmpPath, content);
        File.Move(tmpPath, path, overwrite: true);
    }
}
