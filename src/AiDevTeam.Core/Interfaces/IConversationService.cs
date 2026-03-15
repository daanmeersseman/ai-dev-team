using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

public interface IConversationService
{
    Task<List<Conversation>> GetAllAsync();
    Task<Conversation?> GetByIdAsync(string id);
    Task<Conversation> CreateAsync(string title, string description, Priority priority = Priority.Medium, string? tags = null, string? teamId = null);
    Task<Conversation> UpdateAsync(Conversation conversation);
    Task UpdateStatusAsync(string id, ConversationStatus status);
    Task DeleteAsync(string id);
}
