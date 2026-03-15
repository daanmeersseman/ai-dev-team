using AiDevTeam.Core.Contracts;
using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;
using AiDevTeam.Core.Models.Workflow;

namespace AiDevTeam.Infrastructure.Services.Workflow;

/// <summary>
/// Composes human-readable chat messages from structured workflow events.
/// This is what makes the team conversation feel natural and followable.
/// </summary>
public class ChatMessageComposer : IChatMessageComposer
{
    public WorkflowChatMessage ComposeDelegation(
        AgentDefinition orchestrator, AgentDefinition target, FlowStep step, string taskTitle, string? summary = null)
    {
        var targetName = GetFirstName(target.Name);

        var chatTemplate = step.ChatTemplate;
        string content;

        if (!string.IsNullOrWhiteSpace(chatTemplate))
        {
            content = chatTemplate
                .Replace("{agent}", targetName)
                .Replace("{previousAgent}", "")
                .Replace("{summary}", summary ?? "")
                .Replace("{taskTitle}", taskTitle);
        }
        else
        {
            // When an explicit summary is provided, append it for context.
            // Otherwise, use concise role-appropriate defaults (not the raw flow step action).
            var coderContext = summary ?? "";
            var reviewerContext = summary ?? "Focus on quality, security and best practices.";
            var testerContext = summary ?? "Validate the implementation.";
            var dbContext = summary ?? "";
            var defaultContext = summary ?? step.Action;

            content = step.AgentRole switch
            {
                "Coder" => string.Format(WorkflowStrings.DelegationCoder, targetName, coderContext).TrimEnd(),
                "Reviewer" => string.Format(WorkflowStrings.DelegationReviewer, targetName, reviewerContext).TrimEnd(),
                "Tester" => string.Format(WorkflowStrings.DelegationTester, targetName, testerContext).TrimEnd(),
                "DatabaseSpecialist" => string.Format(WorkflowStrings.DelegationDbSpecialist, targetName, dbContext).TrimEnd(),
                _ => string.Format(WorkflowStrings.DelegationDefault, targetName, defaultContext).TrimEnd()
            };
        }

        return new WorkflowChatMessage
        {
            Sender = orchestrator.Name,
            SenderRole = orchestrator.Role.ToString(),
            Content = content,
            MessageType = nameof(MessageType.WorkflowDelegation)
        };
    }

    public WorkflowChatMessage ComposeOptionsPresentation(AgentDefinition orchestrator, OrchestratorResult analysis)
    {
        var intro = !string.IsNullOrWhiteSpace(analysis.ChatMessage) ? analysis.ChatMessage : analysis.Summary;
        var lines = new List<string> { intro, "" };

        foreach (var opt in analysis.Options)
        {
            var rec = opt.IsRecommended ? $" **{WorkflowStrings.Recommended}**" : "";
            lines.Add($"**{opt.Number}. {opt.Title}**{rec}");
            lines.Add($"   {opt.Description}");
            if (!string.IsNullOrWhiteSpace(opt.Pros))
                lines.Add($"   {WorkflowStrings.Pros}: {opt.Pros}");
            if (!string.IsNullOrWhiteSpace(opt.Cons))
                lines.Add($"   {WorkflowStrings.Cons}: {opt.Cons}");
            lines.Add("");
        }

        lines.Add(WorkflowStrings.OptionsQuestion);

        return new WorkflowChatMessage
        {
            Sender = orchestrator.Name,
            SenderRole = orchestrator.Role.ToString(),
            Content = string.Join("\n", lines),
            MessageType = nameof(MessageType.WorkflowOptions)
        };
    }

    public WorkflowChatMessage ComposeQuestions(AgentDefinition orchestrator, OrchestratorResult analysis)
    {
        var intro = !string.IsNullOrWhiteSpace(analysis.ChatMessage) ? analysis.ChatMessage : analysis.Summary;
        var lines = new List<string> { intro, "" };
        lines.Add(WorkflowStrings.QuestionsIntro);
        lines.Add("");
        for (int i = 0; i < analysis.Questions.Count; i++)
            lines.Add($"{i + 1}. {analysis.Questions[i]}");

        return new WorkflowChatMessage
        {
            Sender = orchestrator.Name,
            SenderRole = orchestrator.Role.ToString(),
            Content = string.Join("\n", lines),
            MessageType = nameof(MessageType.WorkflowQuestion)
        };
    }

    public WorkflowChatMessage ComposePlanPresentation(AgentDefinition orchestrator, OrchestratorResult analysis)
    {
        // Content is ONLY the natural language chatMessage — no JSON, no structured data.
        // Structured plan data lives in DetailedContext for programmatic use.
        var content = !string.IsNullOrWhiteSpace(analysis.ChatMessage)
            ? analysis.ChatMessage
            : analysis.Summary;

        return new WorkflowChatMessage
        {
            Sender = orchestrator.Name,
            SenderRole = orchestrator.Role.ToString(),
            Content = content.Trim(),
            MessageType = nameof(MessageType.Plan),
            DetailedContext = analysis.Plan,
            DetailedContextLabel = WorkflowStrings.PlanLabel
        };
    }

    public WorkflowChatMessage ComposeStepComplete(AgentDefinition agent, string summary, string? detailedJson = null)
    {
        return new WorkflowChatMessage
        {
            Sender = agent.Name,
            SenderRole = agent.Role.ToString(),
            Content = summary,
            MessageType = nameof(MessageType.WorkflowStepComplete),
            DetailedContext = detailedJson,
            DetailedContextLabel = string.Format(WorkflowStrings.StepResultLabel, agent.Role)
        };
    }

    public WorkflowChatMessage ComposeReviewResult(AgentDefinition reviewer, ReviewResult result)
    {
        var lines = new List<string> { result.Summary, "" };

        if (result.MustFix.Count > 0)
        {
            lines.Add($"**{WorkflowStrings.MustFix}:**");
            foreach (var issue in result.MustFix)
                lines.Add($"- {issue.Description}" + (issue.FilePath != null ? $" ({issue.FilePath})" : ""));
            lines.Add("");
        }

        if (result.NiceToHave.Count > 0)
        {
            lines.Add($"**{WorkflowStrings.Suggestions}:**");
            foreach (var issue in result.NiceToHave)
                lines.Add($"- {issue.Description}");
            lines.Add("");
        }

        var verdict = result.Decision switch
        {
            ReviewDecision.Approved => WorkflowStrings.ReviewApproved,
            ReviewDecision.ApprovedWithSuggestions => WorkflowStrings.ReviewApprovedWithSuggestions,
            ReviewDecision.ChangesRequired => WorkflowStrings.ReviewChangesRequired,
            ReviewDecision.Blocked => WorkflowStrings.ReviewBlocked,
            _ => result.Decision.ToString()
        };
        lines.Add($"**{WorkflowStrings.Verdict}:** {verdict}");

        return new WorkflowChatMessage
        {
            Sender = reviewer.Name,
            SenderRole = reviewer.Role.ToString(),
            Content = string.Join("\n", lines),
            MessageType = nameof(MessageType.Review)
        };
    }

    public WorkflowChatMessage ComposeTestResult(AgentDefinition tester, TesterResult result)
    {
        var total = result.TestsPassed + result.TestsFailed + result.TestsSkipped;
        var lines = new List<string>
        {
            result.Summary,
            "",
            $"**{WorkflowStrings.TestResults}:** {result.TestsPassed}/{total} {WorkflowStrings.TestPassed}" +
                (result.TestsFailed > 0 ? $", {result.TestsFailed} {WorkflowStrings.TestFailed}" : "") +
                (result.TestsSkipped > 0 ? $", {result.TestsSkipped} {WorkflowStrings.TestSkipped}" : "")
        };

        if (result.Failures.Count > 0)
        {
            lines.Add("");
            lines.Add($"**{WorkflowStrings.FailedTests}:**");
            foreach (var f in result.Failures)
                lines.Add($"- `{f.TestName}`: {f.Reason}");
        }

        return new WorkflowChatMessage
        {
            Sender = tester.Name,
            SenderRole = tester.Role.ToString(),
            Content = string.Join("\n", lines),
            MessageType = nameof(MessageType.TestResult)
        };
    }

    public WorkflowChatMessage ComposeSuggestionsApproval(AgentDefinition orchestrator, AgentDefinition reviewer, AgentDefinition coder, List<string> suggestions)
    {
        var reviewerName = GetFirstName(reviewer.Name);
        var coderName = GetFirstName(coder.Name);
        var lines = new List<string>
        {
            string.Format(WorkflowStrings.SuggestionsIntro, reviewerName, coderName),
            "",
            WorkflowStrings.SuggestionsListHeader
        };

        foreach (var suggestion in suggestions)
            lines.Add($"- {suggestion}");

        lines.Add("");
        lines.Add(WorkflowStrings.SuggestionsQuestion);

        return new WorkflowChatMessage
        {
            Sender = orchestrator.Name,
            SenderRole = orchestrator.Role.ToString(),
            Content = string.Join("\n", lines),
            MessageType = nameof(MessageType.WorkflowQuestion)
        };
    }

    public WorkflowChatMessage ComposeError(string sender, string senderRole, string error)
    {
        return new WorkflowChatMessage
        {
            Sender = sender,
            SenderRole = senderRole,
            Content = string.Format(WorkflowStrings.ErrorOccurred, error),
            MessageType = nameof(MessageType.WorkflowError)
        };
    }

    private static string GetFirstName(string fullName)
        => fullName.Split(' ', '(')[0].Trim();
}
