namespace Trax.Samples.Recovery.Auth;

/// <summary>
/// The roles this sample knows. The page signs in as a <see cref="User"/>; an <see cref="Operator"/> uses the
/// <c>operations</c> namespace and the dashboard (junction answers, re-queue, resume, ask afresh).
/// </summary>
public static class RecoveryRoles
{
    public const string Operator = "Operator";

    /// <summary>
    /// What the page signs in as: it may start a run, arm a crash, change its own run's data, follow its own runs
    /// through runProgress, and drive its own topic map and food order. Nothing under <c>operations</c>.
    /// </summary>
    public const string User = "recovery-user";

    /// <summary>May follow a scenario run's steps through the broadcast view, without answers.</summary>
    public const string Viewer = "Viewer";
}
