namespace Trax.Samples.Board.Auth;

/// <summary>
/// One role per lane. Each agent holds exactly one, so the role a caller holds is the lane it works; the reviewer is a
/// person, who also files tickets.
/// </summary>
public static class BoardRoles
{
    public const string Triage = "triage";

    public const string Reproducer = "reproducer";

    public const string Fixer = "fixer";

    public const string Qa = "qa";

    public const string Reviewer = "reviewer";

    /// <summary>May file a ticket.</summary>
    public const string Reporter = "reporter";

    /// <summary>The operations console and its namespace.</summary>
    public const string Operator = "Operator";

    /// <summary>Every lane's role, for an operation any worker on the board may call.</summary>
    public const string AnyLane =
        Triage + "," + Reproducer + "," + Fixer + "," + Qa + "," + Reviewer;
}
