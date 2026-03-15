using AiDevTeam.Core.Models.Workflow;

namespace AiDevTeam.Core.Interfaces;

public interface IWorkflowExecutionService
{
    Task<WorkflowExecution?> GetByConversationAsync(string conversationId);
    Task SaveAsync(WorkflowExecution execution);
    Task DeleteAsync(string conversationId);
    Task<string> SaveStepInputAsync(string conversationId, string stepId, string jsonContent);
    Task<string> SaveStepOutputAsync(string conversationId, string stepId, string jsonContent);
    Task<string?> ReadStepDataAsync(string filePath);
}
