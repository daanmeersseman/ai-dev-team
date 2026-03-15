using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;

namespace AiDevTeam.Infrastructure.Services;

public class MessageService : IMessageService
{
    private readonly StoragePaths _paths;

    public MessageService(StoragePaths paths) => _paths = paths;

    public async Task<List<Message>> GetByConversationAsync(string conversationId)
    {
        var file = _paths.MessagesFile(conversationId);
        var messages = await JsonStore.LoadListAsync<Message>(file);
        return messages.OrderBy(m => m.CreatedAt).ToList();
    }

    public async Task<Message> AddAsync(AddMessageRequest request)
    {
        var message = new Message
        {
            ConversationId = request.ConversationId,
            Sender = request.Sender,
            SenderRole = request.SenderRole,
            Type = request.Type,
            Content = request.Content,
            RelatedArtifactId = request.RelatedArtifactId,
            AgentRunId = request.AgentRunId,
            MetadataJson = request.MetadataJson
        };

        var file = _paths.MessagesFile(request.ConversationId);
        Directory.CreateDirectory(_paths.ConversationDir(request.ConversationId));
        await JsonStore.UpdateListAsync<Message>(file, list =>
        {
            list.Add(message);
            return list;
        });
        return message;
    }

#pragma warning disable CS0618 // Obsolete
    public Task<Message> AddAsync(string conversationId, string sender, string senderRole,
        MessageType type, string content, string? relatedArtifactId = null,
        string? agentRunId = null, string? metadataJson = null)
    {
        return AddAsync(new AddMessageRequest
        {
            ConversationId = conversationId,
            Sender = sender,
            SenderRole = senderRole,
            Type = type,
            Content = content,
            RelatedArtifactId = relatedArtifactId,
            AgentRunId = agentRunId,
            MetadataJson = metadataJson
        });
    }
#pragma warning restore CS0618
}
