namespace AiDevTeam.Core.Contracts;

public enum ReviewDecision
{
    Approved,
    ApprovedWithSuggestions,
    ChangesRequired,
    Blocked
}

public enum TestDecision
{
    AllPassed,
    SomeFailed,
    Blocked
}
