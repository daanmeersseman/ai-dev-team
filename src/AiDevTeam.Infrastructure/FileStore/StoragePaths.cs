namespace AiDevTeam.Infrastructure.FileStore;

public class StoragePaths
{
    private readonly string _basePath;

    public StoragePaths(string basePath)
    {
        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    public string BasePath => _basePath;
    public string SettingsFile => Path.Combine(_basePath, "settings.json");
    public string AgentsDir => Path.Combine(_basePath, "agents");
    public string TeamsDir => Path.Combine(_basePath, "teams");
    public string ProvidersDir => Path.Combine(_basePath, "providers");
    public string ConversationsDir => Path.Combine(_basePath, "conversations");

    public string AgentFile(string id) => Path.Combine(AgentsDir, $"{id}.json");
    public string TeamFile(string id) => Path.Combine(TeamsDir, $"{id}.json");
    public string ProviderFile(string name) => Path.Combine(ProvidersDir, $"{name.ToLowerInvariant()}.json");
    public string ConversationDir(string id) => Path.Combine(ConversationsDir, id);
    public string ConversationFile(string id) => Path.Combine(ConversationDir(id), "conversation.json");
    public string MessagesFile(string id) => Path.Combine(ConversationDir(id), "messages.json");
    public string RunsFile(string id) => Path.Combine(ConversationDir(id), "runs.json");
    public string ArtifactsMetaFile(string id) => Path.Combine(ConversationDir(id), "artifacts.json");
    public string ContextBlocksFile(string conversationId) => Path.Combine(ConversationDir(conversationId), "context-blocks.json");
    public string AgentContextDir(string conversationId) => Path.Combine(ConversationDir(conversationId), "context");
    public string AgentContextFile(string conversationId, string agentId) => Path.Combine(AgentContextDir(conversationId), $"{agentId}.json");

    // Workflow
    public string WorkflowFile(string conversationId) => Path.Combine(ConversationDir(conversationId), "workflow.json");
    public string WorkflowStepsDir(string conversationId) => Path.Combine(ConversationDir(conversationId), "workflow-steps");
    public string StepInputFile(string conversationId, string stepId) => Path.Combine(WorkflowStepsDir(conversationId), $"{stepId}.input.json");
    public string StepOutputFile(string conversationId, string stepId) => Path.Combine(WorkflowStepsDir(conversationId), $"{stepId}.output.json");
}
