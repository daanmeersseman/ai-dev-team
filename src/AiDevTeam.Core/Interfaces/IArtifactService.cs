using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

public interface IArtifactService
{
    Task<List<Artifact>> GetByConversationAsync(string conversationId);
    Task<Artifact> CreateAsync(string conversationId, string fileName, ArtifactType type, string content, string? createdByAgent = null);
    Task<Artifact> UpdateContentAsync(string artifactId, string conversationId, string content, string? modifiedByAgent = null);
    Task<string> GetContentAsync(string conversationId, string fileName);
    string GetArtifactDirectory(string conversationId);
}
