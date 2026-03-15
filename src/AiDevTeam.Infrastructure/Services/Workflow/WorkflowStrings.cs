namespace AiDevTeam.Infrastructure.Services.Workflow;

/// <summary>
/// Centralized string constants for workflow messages.
/// All user-facing strings are English by default.
/// Users can override per-step via FlowStep.ChatTemplate.
/// </summary>
internal static class WorkflowStrings
{
    // Delegation defaults (used when no ChatTemplate is set)
    public const string DelegationCoder = "{0}, can you handle the implementation? {1}";
    public const string DelegationReviewer = "{0}, can you review the changes? {1}";
    public const string DelegationTester = "{0}, can you write and run tests? {1}";
    public const string DelegationDbSpecialist = "{0}, can you review the database impact? {1}";
    public const string DelegationDefault = "{0}, {1}";

    // Options presentation
    public const string Recommended = "(recommended)";
    public const string Pros = "Pros";
    public const string Cons = "Cons";
    public const string OptionsQuestion = "Which approach do you prefer?";

    // Questions
    public const string QuestionsIntro = "I have a few questions before I can proceed:";

    // Plan presentation
    public const string Risks = "Risks";
    public const string Complexity = "Complexity";
    public const string PlanLabel = "Implementation plan";

    // Review verdicts
    public const string ReviewApproved = "Approved!";
    public const string ReviewApprovedWithSuggestions = "Approved with suggestions.";
    public const string ReviewChangesRequired = "Changes required.";
    public const string ReviewBlocked = "Blocked.";
    public const string MustFix = "Must fix";
    public const string Suggestions = "Suggestions";
    public const string Verdict = "Verdict";

    // Test results
    public const string TestResults = "Results";
    public const string TestPassed = "passed";
    public const string TestFailed = "failed";
    public const string TestSkipped = "skipped";
    public const string FailedTests = "Failed tests";

    // Step complete
    public const string StepResultLabel = "{0} result";

    // Errors
    public const string ErrorOccurred = "An error occurred: {0}";

    // Suggestions approval
    public const string SuggestionsIntro = "{0} approved the code but had some suggestions. Would you like {1} to implement these before we move to testing?";
    public const string SuggestionsListHeader = "**Suggestions:**";
    public const string SuggestionsQuestion = "Reply **yes** to implement them, or **no** to skip and proceed to testing.";

    // Plan approval
    public const string PlanApprovalMessage = "That's the plan! Shall I get the team started? Reply **go** to proceed or share any feedback.";
    public const string PlanApprovalQuestion = "Approve this plan?";

    // Workflow engine
    public const string WorkflowCompleted = "Workflow completed! All steps finished successfully.";
    public const string FallbackAnalysis = "I've analyzed the task. Let me proceed with the implementation.";
    public const string OptionSelected = "I chose option {0}: {1}";
}
