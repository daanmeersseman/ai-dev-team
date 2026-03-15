using AiDevTeam.Core.Models;

namespace AiDevTeam.Core.Interfaces;

public interface IContextBlockService
{
    Task<ContextBlock> CreateAsync(string conversationId, string messageId, string label, string content, string agentName);
    Task<List<ContextBlock>> GetByConversationAsync(string conversationId);
    Task<ContextBlock?> GetByIdAsync(string conversationId, string blockId);
}
