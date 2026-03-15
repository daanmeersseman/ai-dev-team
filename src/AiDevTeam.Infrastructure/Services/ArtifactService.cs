using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;

namespace AiDevTeam.Infrastructure.Services;

public class ArtifactService : IArtifactService
{
    private readonly StoragePaths _paths;

    public ArtifactService(StoragePaths paths) => _paths = paths;

    public string GetArtifactDirectory(string conversationId)
    {
        var dir = _paths.ConversationDir(conversationId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<List<Artifact>> GetByConversationAsync(string conversationId)
    {
        var file = _paths.ArtifactsMetaFile(conversationId);
        var artifacts = await JsonStore.LoadListAsync<Artifact>(file);
        return artifacts.OrderBy(a => a.CreatedAt).ToList();
    }

    public async Task<Artifact> CreateAsync(string conversationId, string fileName, ArtifactType type, string content, string? createdByAgent = null)
    {
        var dir = GetArtifactDirectory(conversationId);
        var filePath = Path.Combine(dir, fileName);
        var fileDir = Path.GetDirectoryName(filePath);
        if (fileDir != null) Directory.CreateDirectory(fileDir);
        await File.WriteAllTextAsync(filePath, content);

        var artifact = new Artifact
        {
            ConversationId = conversationId,
            FileName = fileName,
            DisplayName = Path.GetFileNameWithoutExtension(fileName),
            Type = type,
            CreatedByAgent = createdByAgent,
            LastModifiedByAgent = createdByAgent,
            FileSizeBytes = new FileInfo(filePath).Length
        };

        var metaFile = _paths.ArtifactsMetaFile(conversationId);
        await JsonStore.UpdateListAsync<Artifact>(metaFile, list =>
        {
            list.Add(artifact);
            return list;
        });
        return artifact;
    }

    public async Task<Artifact> UpdateContentAsync(string artifactId, string conversationId, string content, string? modifiedByAgent = null)
    {
        var metaFile = _paths.ArtifactsMetaFile(conversationId);
        Artifact? updated = null;
        await JsonStore.UpdateListAsync<Artifact>(metaFile, list =>
        {
            var artifact = list.FirstOrDefault(a => a.Id == artifactId)
                ?? throw new InvalidOperationException($"Artifact {artifactId} not found");

            var filePath = Path.Combine(GetArtifactDirectory(conversationId), artifact.FileName);
            File.WriteAllText(filePath, content);

            artifact.LastModifiedByAgent = modifiedByAgent;
            artifact.FileSizeBytes = new FileInfo(filePath).Length;
            artifact.UpdatedAt = DateTime.UtcNow;
            updated = artifact;
            return list;
        });
        return updated!;
    }

    public async Task<string> GetContentAsync(string conversationId, string fileName)
    {
        var filePath = Path.Combine(GetArtifactDirectory(conversationId), fileName);
        return File.Exists(filePath) ? await File.ReadAllTextAsync(filePath) : string.Empty;
    }
}
