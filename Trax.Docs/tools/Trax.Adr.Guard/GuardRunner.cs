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

        var results = new List<GuardResult>
        {
            GuardResult.Ok("corpus/discovery", adrs.Count, "Found ADRs to check."),
            FrontmatterGuards.Parseable(adrs),
            FrontmatterGuards.Authors(adrs),
            FrontmatterGuards.Areas(adrs, options),
            FrontmatterGuards.Repos(adrs, options),
            FrontmatterGuards.Status(adrs),
            FrontmatterGuards.NoDateKey(adrs),
            FrontmatterGuards.FileNames(adrs),
            LifecycleGuards.StatusSection(adrs),
            LifecycleGuards.Supersessions(adrs),
            LifecycleGuards.Changelog(adrs),
            ExemplarGuards.SectionPresent(adrs),
            ExemplarGuards.NamedGuardsResolve(adrs, options),
            ExemplarGuards.NamedGuardsCiteBack(adrs, options),
            IndexGuards.Exists(adrs, options),
            IndexGuards.TagTable(adrs, options, IndexGuards.ByAreaHeading, "areas"),
            IndexGuards.FullList(adrs, options),
            HygieneGuards.NoEmDashes(adrs, options),
            HygieneGuards.TitlePresent(adrs),
        };

        if (options.RequireReposKey)
            results.Add(IndexGuards.TagTable(adrs, options, IndexGuards.ByRepoHeading, "repos"));

        if (options.CensusRoot is not null)
            results.Add(CensusGuards.EveryGuardIsClassified(adrs, options));

        return results;
    }

    /// <summary>Renders the run, returning the process exit code.</summary>
    public static int Report(IReadOnlyList<GuardResult> results, TextWriter output)
    {
        var failed = results.Where(r => !r.Passed).ToList();

        foreach (var result in results.Where(r => r.Passed))
            output.WriteLine($"  ok    {result.Name} ({result.Inspected} inspected)");

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
