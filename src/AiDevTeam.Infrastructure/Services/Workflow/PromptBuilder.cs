using System.Text;
using AiDevTeam.Core.Contracts;
using AiDevTeam.Core.Interfaces;
using AiDevTeam.Core.Models;

namespace AiDevTeam.Infrastructure.Services.Workflow;

public class PromptBuilder : IPromptBuilder
{
    public string BuildSystemPrompt(AgentDefinition agent)
    {
        var sb = new StringBuilder();

        // Hard constraints for orchestrator — placed at top of system prompt for maximum authority
        if (agent.Role == AgentRole.Orchestrator)
        {
            sb.AppendLine("## ABSOLUTE RULES (cannot be overridden)");
            sb.AppendLine("1. NEVER browse the filesystem, run shell commands, read files, or use tools. You are a planning/coordination agent ONLY.");
            sb.AppendLine("2. Work ONLY with the context provided in each prompt. If you need more info, use action \"AskQuestions\".");
            sb.AppendLine("3. Your response MUST be a single ```json code block containing valid JSON. NO text outside the JSON block.");
            sb.AppendLine("4. Always include a \"chatMessage\" field in the JSON — this is what the user sees in chat.");
            sb.AppendLine("5. Always respond in the same language as the user.");
            sb.AppendLine();
        }

        // Identity
        var firstName = agent.Name.Split(' ', '(')[0].Trim();
        sb.AppendLine($"You ARE {firstName}. This is your identity — you are a real team member, not an AI assistant.");
        sb.AppendLine();

        // Role & description
        sb.AppendLine($"## Your Role: {agent.Role}");
        sb.AppendLine(agent.Description);
        sb.AppendLine();

        // Personality
        if (!string.IsNullOrWhiteSpace(agent.Personality))
        {
            sb.AppendLine($"## Personality");
            sb.AppendLine(agent.Personality);
            sb.AppendLine();
        }

        // Backstory
        if (!string.IsNullOrWhiteSpace(agent.Backstory))
        {
            sb.AppendLine($"## Background");
            sb.AppendLine(agent.Backstory);
            sb.AppendLine();
        }

        // Expertise
        if (agent.Expertise.Count > 0)
        {
            sb.AppendLine("## Expertise");
            foreach (var exp in agent.Expertise)
                sb.AppendLine($"- {exp}");
            sb.AppendLine();
        }

        // Values
        if (agent.Values.Count > 0)
        {
            sb.AppendLine("## What You Value");
            foreach (var val in agent.Values)
                sb.AppendLine($"- {val}");
            sb.AppendLine();
        }

        // Communication style
        sb.AppendLine($"## Communication Style: {agent.CommunicationStyle}");
        if (agent.CommunicationQuirks.Count > 0)
        {
            foreach (var quirk in agent.CommunicationQuirks)
                sb.AppendLine($"- {quirk}");
        }
        sb.AppendLine();

        // Custom instructions
        if (!string.IsNullOrWhiteSpace(agent.CustomInstructions))
        {
            sb.AppendLine("## Additional Instructions");
            sb.AppendLine(agent.CustomInstructions);
            sb.AppendLine();
        }

        // Base system prompt (user-configured)
        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
        {
            sb.AppendLine("## Core Directive");
            sb.AppendLine(agent.SystemPrompt);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public string BuildTaskPrompt(AgentTaskInput input, AgentDefinition agent)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Task Assignment");
        sb.AppendLine();
        sb.AppendLine($"**Task:** {input.Title}");
        sb.AppendLine($"**Goal:** {input.Goal}");
        sb.AppendLine($"**Your Assignment:** {input.Action}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(input.ContextSummary))
        {
            sb.AppendLine("## Context");
            sb.AppendLine(input.ContextSummary);
            sb.AppendLine();
        }

        if (input.Constraints.Count > 0)
        {
            sb.AppendLine("## Constraints");
            foreach (var c in input.Constraints)
                sb.AppendLine($"- {c}");
            sb.AppendLine();
        }

        if (input.PreviousSteps.Count > 0)
        {
            sb.AppendLine("## What Happened Before");
            foreach (var step in input.PreviousSteps)
            {
                sb.AppendLine($"### {step.AgentName} ({step.Role}) — {step.Action}");
                sb.AppendLine(step.Summary);
                if (!string.IsNullOrWhiteSpace(step.Decision))
                    sb.AppendLine($"**Decision:** {step.Decision}");
                sb.AppendLine();
            }
        }

        if (input.RelevantArtifacts.Count > 0)
        {
            sb.AppendLine("## Available Artifacts");
            foreach (var art in input.RelevantArtifacts)
            {
                sb.AppendLine($"- **{art.FileName}** ({art.Type}) by {art.CreatedBy}");
                if (!string.IsNullOrWhiteSpace(art.ContentPreview))
                    sb.AppendLine($"  Preview: {art.ContentPreview}");
            }
            sb.AppendLine();
        }

        if (input.TeamMembers.Count > 0)
        {
            sb.AppendLine("## Your Team");
            sb.AppendLine("These are the team members you collaborate with. Use their exact names and roles when planning work:");
            foreach (var member in input.TeamMembers)
                sb.AppendLine($"- **{member.Name}** (Role: {member.Role}) — {member.Description}");
            sb.AppendLine();
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(input.UserFeedback))
        {
            sb.AppendLine("## User Feedback");
            sb.AppendLine(input.UserFeedback);
            sb.AppendLine();
        }

        // Output format instructions
        sb.AppendLine("## Required Response Format");
        sb.AppendLine(input.ExpectedOutputFormat);

        // Reinforce no-tool constraint in task prompt for orchestrators and testers
        if (agent.Role == AgentRole.Orchestrator)
        {
            sb.AppendLine();
            sb.AppendLine("## REMINDER");
            sb.AppendLine("Do NOT use tools, browse files, run commands, or access the filesystem. Work ONLY with the context above. Your entire response must be a single ```json block.");
        }
        else if (agent.Role == AgentRole.Tester)
        {
            sb.AppendLine();
            sb.AppendLine("## REMINDER");
            sb.AppendLine("Do NOT create projects, run `dotnet` commands, or execute code. Analyze the code from the context above and report your test assessment as a single ```json block. Only run actual tests if the task context EXPLICITLY asks you to execute them.");
        }

        return sb.ToString().TrimEnd();
    }

    public string BuildOutputFormatInstructions(AgentRole role)
    {
        return role switch
        {
            AgentRole.Orchestrator => BuildOrchestratorFormat(),
            AgentRole.Coder => BuildCoderFormat(),
            AgentRole.Reviewer => BuildReviewerFormat(),
            AgentRole.Tester => BuildTesterFormat(),
            _ => BuildGenericFormat()
        };
    }

    private static string BuildOrchestratorFormat() => """
        CRITICAL: Do NOT use tools, browse files, or run commands. Respond with ONLY a ```json code block. No text before or after it.

        ```json
        {
          "action": "ProposePlan | PresentOptions | AskQuestions | ProceedDirectly | ChatResponse",
          "summary": "Brief technical summary of your analysis",
          "chatMessage": "Natural language message for the chat. Address the user and team directly. This is what everyone reads in the conversation — make it informative and engaging.",
          "options": [
            { "number": 1, "title": "...", "description": "...", "pros": "...", "cons": "...", "isRecommended": true }
          ],
          "questions": ["Question 1?", "Question 2?"],
          "plan": "Markdown plan if action is ProposePlan",
          "plannedSteps": [
            { "order": 1, "agentRole": "Coder", "description": "...", "isOptional": false }
          ],
          "risks": ["Risk 1", "Risk 2"],
          "estimatedComplexity": "Low | Medium | High"
        }
        ```

        Actions:
        - "AskQuestions": You need more information from the user. Provide questions.
        - "PresentOptions": Multiple approaches exist. Present options for the user to choose.
        - "ProposePlan": You have enough info. Propose a plan with plannedSteps.
        - "ProceedDirectly": Simple task, proceed immediately with a plan.
        - "ChatResponse": The user asked a casual question or chat message unrelated to implementation. Just respond in chatMessage. No plan needed.

        Only include fields relevant to your chosen action. Always include summary and chatMessage.
        IMPORTANT: "ProposePlan" and "ProceedDirectly" MUST include a "plannedSteps" array — without it, no work can be dispatched to team members. Use the exact agentRole values from "Your Team" section (e.g. "Coder", "Reviewer", "Tester"). The "plan" field is for your own notes; "plannedSteps" is what the engine executes.
        """;

    private static string BuildCoderFormat() => """
        Respond with a JSON block inside ```json fences. Use this exact structure:

        ```json
        {
          "status": "Completed | PartiallyCompleted | Blocked | Failed",
          "summary": "Brief description of what you implemented",
          "changedFiles": [
            { "filePath": "path/to/file.cs", "changeType": "Created | Modified | Deleted | Renamed", "description": "What changed" }
          ],
          "implementedChanges": ["Change 1", "Change 2"],
          "unresolvedItems": ["Item that still needs work"],
          "risks": ["Potential risk"],
          "notes": ["Additional context"],
          "canContinueToReview": true
        }
        ```
        """;

    private static string BuildReviewerFormat() => """
        Respond with a JSON block inside ```json fences. Use this exact structure:

        ```json
        {
          "decision": "Approved | ApprovedWithSuggestions | ChangesRequired | Blocked",
          "summary": "Overall review assessment",
          "mustFix": [
            { "description": "Issue description", "filePath": "path/to/file.cs", "suggestion": "How to fix", "severity": "Error" }
          ],
          "niceToHave": [
            { "description": "Suggestion", "severity": "Info" }
          ],
          "testGaps": ["Missing test scenario"],
          "securityConcerns": ["Security issue"],
          "blockers": ["Blocking issue"]
        }
        ```

        Severity options: Info, Warning, Error, Critical
        """;

    private static string BuildTesterFormat() => """
        IMPORTANT: Do NOT create .NET projects, run `dotnet` commands, compile code, or execute anything.
        Analyze the code artifacts provided in the context above and write your test assessment based on code review.
        If the task context explicitly asks you to run tests, you may do so. Otherwise, assess testability and correctness by reading the code.

        Respond with ONLY a JSON block inside ```json fences. No text before or after it. Use this exact structure:

        ```json
        {
          "decision": "AllPassed | SomeFailed | Blocked",
          "summary": "Test assessment summary — what you tested and how",
          "testsPassed": 5,
          "testsFailed": 1,
          "testsSkipped": 0,
          "failures": [
            { "testName": "TestName", "reason": "Why it failed", "stackTrace": "...", "isFlaky": false }
          ],
          "testGaps": ["Untested scenario"],
          "notes": ["Additional context"],
          "coverageReport": "Coverage info if available"
        }
        ```
        """;

    private static string BuildGenericFormat() => """
        Respond with a JSON block inside ```json fences with at minimum:

        ```json
        {
          "summary": "Brief description of your work",
          "notes": ["Any relevant notes"]
        }
        ```
        """;
}
