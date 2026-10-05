namespace Trax.Samples.Recovery.Auth;

/// <summary>
/// The one role this sample knows. An operator may start runs, arm a crash, change a run's data,
/// and use the <c>operations</c> namespace (junction answers, re-queue, ask afresh).
/// </summary>
public static class RecoveryRoles
{
    public const string Operator = "Operator";

    /// <summary>May follow a scenario run's steps through the broadcast view, without answers.</summary>
    public const string Viewer = "Viewer";
}
