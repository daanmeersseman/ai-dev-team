using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;

namespace AiDevTeam.Infrastructure.Services;

public class ConversationService : IConversationService
{
    private readonly StoragePaths _paths;

    public ConversationService(StoragePaths paths) => _paths = paths;

    public async Task<List<Conversation>> GetAllAsync()
    {
        var conversations = new List<Conversation>();
        var dir = _paths.ConversationsDir;
        if (!Directory.Exists(dir)) return conversations;

        foreach (var convDir in Directory.GetDirectories(dir))
        {
            var id = Path.GetFileName(convDir);
            var convFile = _paths.ConversationFile(id);
            if (File.Exists(convFile))
            {
                var conv = await JsonStore.LoadAsync<Conversation>(convFile);
                conversations.Add(conv);
            }
        }
        return conversations.OrderByDescending(c => c.UpdatedAt).ToList();
    }

    public async Task<Conversation?> GetByIdAsync(string id)
    {
        var file = _paths.ConversationFile(id);
        if (!File.Exists(file)) return null;
        return await JsonStore.LoadAsync<Conversation>(file);
    }

    public async Task<Conversation> CreateAsync(string title, string description, Priority priority = Priority.Medium, string? tags = null, string? teamId = null)
    {
        var conversation = new Conversation
        {
            Title = title,
            Description = description,
            Priority = priority,
            Tags = tags,
            TeamId = teamId
        };
        Directory.CreateDirectory(_paths.ConversationDir(conversation.Id));
        await JsonStore.SaveAsync(_paths.ConversationFile(conversation.Id), conversation);
        return conversation;
    }

    public async Task<Conversation> UpdateAsync(Conversation conversation)
    {
        conversation.UpdatedAt = DateTime.UtcNow;
        await JsonStore.SaveAsync(_paths.ConversationFile(conversation.Id), conversation);
        return conversation;
    }

    public async Task UpdateStatusAsync(string id, ConversationStatus status)
    {
        var conv = await GetByIdAsync(id);
        if (conv != null)
        {
            conv.Status = status;
            conv.UpdatedAt = DateTime.UtcNow;
            await JsonStore.SaveAsync(_paths.ConversationFile(id), conv);
        }
    }

    public async Task DeleteAsync(string id)
    {
        var dir = _paths.ConversationDir(id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
    }
}
