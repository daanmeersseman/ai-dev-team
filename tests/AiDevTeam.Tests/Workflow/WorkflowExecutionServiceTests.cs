using AiDevTeam.Core.Models.Workflow;
using AiDevTeam.Infrastructure.FileStore;
using AiDevTeam.Infrastructure.Services.Workflow;

namespace AiDevTeam.Tests.Workflow;

public class WorkflowExecutionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StoragePaths _paths;
    private readonly WorkflowExecutionService _service;

    public WorkflowExecutionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AiDevTeamTests_" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new StoragePaths(_tempDir);
        _service = new WorkflowExecutionService(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task SaveAndLoad_roundtrips_correctly()
    {
        var execution = new WorkflowExecution
        {
            ConversationId = "conv-1",
            CurrentState = WorkflowState.Coding,
            FlowConfigurationName = "Default Flow"
        };

        await _service.SaveAsync(execution);
        var loaded = await _service.GetByConversationAsync("conv-1");

        Assert.NotNull(loaded);
        Assert.Equal(execution.Id, loaded!.Id);
        Assert.Equal(WorkflowState.Coding, loaded.CurrentState);
        Assert.Equal("Default Flow", loaded.FlowConfigurationName);
    }

    [Fact]
    public async Task GetByConversation_returns_null_when_not_found()
    {
        var result = await _service.GetByConversationAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task Save_updates_existing_execution()
    {
        var execution = new WorkflowExecution
        {
            ConversationId = "conv-2",
            CurrentState = WorkflowState.Created
        };
        await _service.SaveAsync(execution);

        execution.CurrentState = WorkflowState.Analyzing;
        await _service.SaveAsync(execution);

        var loaded = await _service.GetByConversationAsync("conv-2");
        Assert.Equal(WorkflowState.Analyzing, loaded!.CurrentState);
    }

    [Fact]
    public async Task Delete_removes_workflow_and_steps()
    {
        var execution = new WorkflowExecution { ConversationId = "conv-3" };
        await _service.SaveAsync(execution);
        await _service.SaveStepInputAsync("conv-3", "step-1", "{\"test\": true}");

        await _service.DeleteAsync("conv-3");

        Assert.Null(await _service.GetByConversationAsync("conv-3"));
    }

    [Fact]
    public async Task SaveStepInput_creates_file_and_returns_path()
    {
        var path = await _service.SaveStepInputAsync("conv-4", "step-1", "{\"prompt\": \"test\"}");

        Assert.True(File.Exists(path));
        Assert.Contains("step-1.input.json", path);
    }

    [Fact]
    public async Task SaveStepOutput_creates_file_and_returns_path()
    {
        var path = await _service.SaveStepOutputAsync("conv-4", "step-1", "{\"result\": \"ok\"}");

        Assert.True(File.Exists(path));
        Assert.Contains("step-1.output.json", path);
    }

    [Fact]
    public async Task ReadStepData_returns_content()
    {
        var json = "{\"hello\": \"world\"}";
        var path = await _service.SaveStepInputAsync("conv-5", "step-1", json);

        var content = await _service.ReadStepDataAsync(path);

        Assert.Equal(json, content);
    }

    [Fact]
    public async Task ReadStepData_returns_null_for_missing_file()
    {
        var content = await _service.ReadStepDataAsync("/nonexistent/path.json");
        Assert.Null(content);
    }

    [Fact]
    public async Task Execution_persists_steps_and_decisions()
    {
        var execution = new WorkflowExecution
        {
            ConversationId = "conv-6",
            CurrentState = WorkflowState.Coding,
            Steps = new()
            {
                new WorkflowStep
                {
                    AgentName = "Sam", AgentRole = "Coder", Action = "Implement",
                    Status = WorkflowStepStatus.Succeeded
                }
            },
            Decisions = new()
            {
                new WorkflowDecision
                {
                    DecisionMaker = "Engine", Type = WorkflowDecisionType.StateTransition,
                    Reason = "Coder done", FromState = WorkflowState.Coding, ToState = WorkflowState.Reviewing
                }
            }
        };

        await _service.SaveAsync(execution);
        var loaded = await _service.GetByConversationAsync("conv-6");

        Assert.Single(loaded!.Steps);
        Assert.Equal("Sam", loaded.Steps[0].AgentName);
        Assert.Single(loaded.Decisions);
        Assert.Equal(WorkflowDecisionType.StateTransition, loaded.Decisions[0].Type);
    }
}
