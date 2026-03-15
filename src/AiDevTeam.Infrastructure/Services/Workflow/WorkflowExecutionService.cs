using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models.Workflow;
using AiDevTeam.Infrastructure.FileStore;

namespace AiDevTeam.Infrastructure.Services.Workflow;

public class WorkflowExecutionService : IWorkflowExecutionService
{
    private readonly StoragePaths _paths;

    public WorkflowExecutionService(StoragePaths paths)
    {
        _paths = paths;
    }

    public async Task<WorkflowExecution?> GetByConversationAsync(string conversationId)
    {
        var path = _paths.WorkflowFile(conversationId);
        if (!File.Exists(path)) return null;
        return await JsonStore.LoadAsync<WorkflowExecution>(path);
    }

    public async Task SaveAsync(WorkflowExecution execution)
    {
        execution.UpdatedAt = DateTime.UtcNow;
        await JsonStore.SaveAsync(_paths.WorkflowFile(execution.ConversationId), execution);
    }

    public Task DeleteAsync(string conversationId)
    {
        var path = _paths.WorkflowFile(conversationId);
        if (File.Exists(path)) File.Delete(path);

        var stepsDir = _paths.WorkflowStepsDir(conversationId);
        if (Directory.Exists(stepsDir)) Directory.Delete(stepsDir, recursive: true);

        return Task.CompletedTask;
    }

    public async Task<string> SaveStepInputAsync(string conversationId, string stepId, string jsonContent)
    {
        var path = _paths.StepInputFile(conversationId, stepId);
        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(path, jsonContent);
        return path;
    }

    public async Task<string> SaveStepOutputAsync(string conversationId, string stepId, string jsonContent)
    {
        var path = _paths.StepOutputFile(conversationId, stepId);
        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(path, jsonContent);
        return path;
    }

    public async Task<string?> ReadStepDataAsync(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        return await File.ReadAllTextAsync(filePath);
    }
}
