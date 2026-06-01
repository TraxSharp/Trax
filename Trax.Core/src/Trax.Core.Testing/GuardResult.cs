namespace Trax.Core.Testing;

/// <summary>
/// The outcome of an architecture-guard check. A guard returns the offenders it found, how many items
/// it inspected (so a misconfigured scan can't silently pass), and a ready-to-use failure message.
/// </summary>
/// <param name="Offenders">Repo-relative offender descriptions (often <c>path:line (reason)</c>).</param>
/// <param name="Inspected">How many candidate items the guard examined.</param>
/// <param name="FailureMessage">A message explaining the rule and how to fix a violation, with the offender list appended.</param>
/// <remarks>
/// Consumers assert on this with their own test framework, e.g.
/// <c>result.Offenders.Should().BeEmpty(result.FailureMessage)</c> and, where a guard must find work,
/// <c>result.Inspected.Should().BeGreaterThan(0)</c>.
/// </remarks>
public sealed record GuardResult(
    IReadOnlyList<string> Offenders,
    int Inspected,
    string FailureMessage
)
{
    /// <summary>True when no offenders were found.</summary>
    public bool Passed => Offenders.Count == 0;
}
