namespace Trax.Adr.Guard;

/// <summary>
/// The outcome of a single ADR check. A check returns the offenders it found, how many
/// items it inspected, and a ready-to-use failure message.
/// </summary>
/// <param name="Name">The check's name, printed in the report.</param>
/// <param name="Offenders">Repo-relative offender descriptions (often <c>path:line (reason)</c>).</param>
/// <param name="Inspected">
/// How many candidate items the check examined. A check that inspected nothing has not
/// passed, it has failed to look: see <see cref="Passed"/>.
/// </param>
/// <param name="FailureMessage">The rule and how to fix a violation. Offenders are appended by the reporter.</param>
public sealed record GuardResult(
    string Name,
    IReadOnlyList<string> Offenders,
    int Inspected,
    string FailureMessage
)
{
    /// <summary>
    /// True when the check found work to do and no offenders in it.
    ///
    /// <para>
    /// An empty corpus is a failure, not a pass. nwyc's equivalent fixture passed nine of
    /// its thirteen assertions over an empty set until a discovery check was added, which
    /// is exactly the shape of bug this property exists to prevent: a renamed directory
    /// turns every "no violations" assertion vacuous at once, and nothing says so.
    /// </para>
    /// </summary>
    public bool Passed => Offenders.Count == 0 && Inspected > 0;

    /// <summary>A check that looked at nothing, which is reported differently from a violation.</summary>
    public bool InspectedNothing => Inspected == 0;

    public static GuardResult Ok(string name, int inspected, string rule) =>
        new(name, [], inspected, rule);
}
