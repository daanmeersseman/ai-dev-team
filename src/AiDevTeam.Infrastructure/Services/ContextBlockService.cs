using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Infrastructure.FileStore;

namespace AiDevTeam.Infrastructure.Services;

public class ContextBlockService : IContextBlockService
{
    private readonly StoragePaths _paths;

    public ContextBlockService(StoragePaths paths) => _paths = paths;

    public async Task<ContextBlock> CreateAsync(string conversationId, string messageId, string label, string content, string agentName)
    {
        var block = new ContextBlock
        {
            ConversationId = conversationId,
            MessageId = messageId,
            Label = label,
            Content = content,
            AgentName = agentName
        };

        var file = _paths.ContextBlocksFile(conversationId);
        Directory.CreateDirectory(_paths.ConversationDir(conversationId));
        await JsonStore.UpdateListAsync<ContextBlock>(file, list =>
        {
            list.Add(block);
            return list;
        });
        return block;
    }

    public async Task<List<ContextBlock>> GetByConversationAsync(string conversationId)
    {
        return await JsonStore.LoadListAsync<ContextBlock>(_paths.ContextBlocksFile(conversationId));
    }

    public async Task<ContextBlock?> GetByIdAsync(string conversationId, string blockId)
    {
        var blocks = await GetByConversationAsync(conversationId);
        return blocks.FirstOrDefault(b => b.Id == blockId);
    }
}
