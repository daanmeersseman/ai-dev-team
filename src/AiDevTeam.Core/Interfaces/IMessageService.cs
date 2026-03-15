using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

public interface IMessageService
{
    Task<List<Message>> GetByConversationAsync(string conversationId);
    Task<Message> AddAsync(AddMessageRequest request);

    [Obsolete("Use AddAsync(AddMessageRequest) instead")]
    Task<Message> AddAsync(string conversationId, string sender, string senderRole, MessageType type, string content, string? relatedArtifactId = null, string? agentRunId = null, string? metadataJson = null);
}
