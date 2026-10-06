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
/// <param name="AllowsEmpty">
/// True when having nothing to inspect is a legitimate state rather than a broken scan.
/// A corpus with no supersessions really has none. Set it deliberately and rarely: it
/// switches off the protection below, so a check that sets it can go vacuous unnoticed.
/// </param>
public sealed record GuardResult(
    string Name,
    IReadOnlyList<string> Offenders,
    int Inspected,
    string FailureMessage,
    bool AllowsEmpty = false
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
    public bool Passed => Offenders.Count == 0 && (Inspected > 0 || AllowsEmpty);

    /// <summary>A check that should have looked at something and did not.</summary>
    public bool InspectedNothing => Inspected == 0 && !AllowsEmpty;

    /// <summary>A check that legitimately had nothing to look at, reported as such.</summary>
    public bool NothingToCheck => Inspected == 0 && AllowsEmpty;

    public static GuardResult Ok(string name, int inspected, string rule) =>
        new(name, [], inspected, rule);
}
