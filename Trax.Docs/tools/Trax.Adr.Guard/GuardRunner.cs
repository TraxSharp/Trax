using Trax.Adr.Guard.Guards;

namespace Trax.Adr.Guard;

/// <summary>
/// Composes the checks and reports them. Separated from the CLI entry point so the guard
/// can be driven directly from its own tests against synthetic corpora.
/// </summary>
public static class GuardRunner
{
    /// <summary>
    /// Runs every configured check. An empty corpus short-circuits to a single discovery
    /// failure rather than a wall of checks that each inspected nothing.
    /// </summary>
    public static IReadOnlyList<GuardResult> Run(GuardOptions options)
    {
        var adrs = AdrCorpus.Discover(options);

        if (adrs.Count == 0)
        {
            return
            [
                new GuardResult(
                    "corpus/discovery",
                    [$"no ADRs found in '{options.AdrRoot}'"],
                    0,
                    "The checks found nothing to check, which is a failure and not a pass: a "
                        + "renamed or empty directory would otherwise turn every result vacuous at "
                        + "once. If this repo genuinely has no decisions recorded yet, remove the "
                        + "adr-guard job until it does."
                ),
            ];
        }

        var checks = new List<(string Name, Func<GuardResult> Run)>
        {
            ("frontmatter/parseable", () => FrontmatterGuards.Parseable(adrs)),
            ("frontmatter/authors", () => FrontmatterGuards.Authors(adrs)),
            ("frontmatter/areas", () => FrontmatterGuards.Areas(adrs, options)),
            ("frontmatter/repos", () => FrontmatterGuards.Repos(adrs, options)),
            ("frontmatter/status", () => FrontmatterGuards.Status(adrs)),
            ("frontmatter/keys", () => FrontmatterGuards.NoDateKey(adrs)),
            ("frontmatter/file-names", () => FrontmatterGuards.FileNames(adrs)),
            ("lifecycle/status-section", () => LifecycleGuards.StatusSection(adrs)),
            ("lifecycle/supersessions", () => LifecycleGuards.Supersessions(adrs)),
            ("lifecycle/changelog", () => LifecycleGuards.Changelog(adrs)),
            ("exemplars/section", () => ExemplarGuards.SectionPresent(adrs)),
            ("exemplars/guards-resolve", () => ExemplarGuards.NamedGuardsResolve(adrs, options)),
            ("exemplars/guards-cite-back", () => ExemplarGuards.NamedGuardsCiteBack(adrs, options)),
            ("index/exists", () => IndexGuards.Exists(adrs, options)),
            (
                "index/areas-table",
                () => IndexGuards.TagTable(adrs, options, IndexGuards.ByAreaHeading, "areas")
            ),
            ("index/full-list", () => IndexGuards.FullList(adrs, options)),
            ("hygiene/no-em-dashes", () => HygieneGuards.NoEmDashes(adrs, options)),
            ("hygiene/title", () => HygieneGuards.TitlePresent(adrs)),
        };

        if (options.RequireReposKey)
            checks.Add(
                (
                    "index/repos-table",
                    () => IndexGuards.TagTable(adrs, options, IndexGuards.ByRepoHeading, "repos")
                )
            );

        if (options.CensusRoot is not null)
            checks.Add(
                ("census/classified", () => CensusGuards.EveryGuardIsClassified(adrs, options))
            );

        var results = new List<GuardResult>
        {
            GuardResult.Ok("corpus/discovery", adrs.Count, "Found ADRs to check."),
        };
        results.AddRange(checks.Select(c => Guarded(c.Name, c.Run)));

        return results;
    }

    /// <summary>
    /// Runs one check, turning an exception into a failed result that names it.
    ///
    /// <para>
    /// A check that threw used to take the whole run with it: the action printed a stack
    /// trace and no report, so the other checks' findings, often including the one that says
    /// what is actually wrong, were never shown. Failing the one check keeps the run closed
    /// and keeps the rest of the report.
    /// </para>
    /// </summary>
    public static GuardResult Guarded(string name, Func<GuardResult> check)
    {
        try
        {
            return check();
        }
        catch (Exception ex)
        {
            return new GuardResult(
                name,
                [$"the check threw {ex.GetType().Name}: {ex.Message}"],
                0,
                "The check could not finish on this corpus. That is a failure, not a pass; the "
                    + "other checks' results are still reported, and one of them usually names "
                    + "what the corpus got wrong."
            );
        }
    }

    /// <summary>Renders the run, returning the process exit code.</summary>
    public static int Report(IReadOnlyList<GuardResult> results, TextWriter output)
    {
        var failed = results.Where(r => !r.Passed).ToList();

        foreach (var result in results.Where(r => r.Passed))
        {
            output.WriteLine(
                result.NothingToCheck
                    ? $"  ok    {result.Name} (nothing to check)"
                    : $"  ok    {result.Name} ({result.Inspected} inspected)"
            );
        }

        foreach (var result in failed)
        {
            output.WriteLine();
            output.WriteLine($"  FAIL  {result.Name}");
            output.WriteLine();
            output.WriteLine(Indent(result.FailureMessage));

            if (result.InspectedNothing && result.Offenders.Count == 0)
            {
                output.WriteLine(
                    Indent("This check inspected nothing, which is a failure and not a pass.")
                );
                continue;
            }

            output.WriteLine();
            foreach (var offender in result.Offenders)
                output.WriteLine($"      {offender}");
        }

        output.WriteLine();
        output.WriteLine(
            failed.Count == 0
                ? $"All {results.Count} ADR checks passed."
                : $"{failed.Count} of {results.Count} ADR checks failed."
        );

        return failed.Count == 0 ? 0 : 1;
    }

    private static string Indent(string text) =>
        string.Join('\n', text.Split('\n').Select(l => "        " + l));
}
