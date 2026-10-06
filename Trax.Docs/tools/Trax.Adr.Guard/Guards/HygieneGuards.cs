namespace Trax.Adr.Guard.Guards;

/// <summary>
/// The tidiness checks. Trax.Docs enforces these over its own pages with
/// <c>NoEmDashesTests</c>, but ADRs live in the repos they bind, where that fixture cannot
/// see them. These carry the same rules to wherever the ADR actually is.
/// </summary>
public static class HygieneGuards
{
    /// <summary>U+2014, the long dash. Not the hyphen, and not U+2013.</summary>
    public const char EmDash = '—';

    public static GuardResult NoEmDashes(IReadOnlyList<Adr> adrs, GuardOptions options)
    {
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var adr in adrs)
        {
            inspected++;
            var lines = adr.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(EmDash))
                    offenders.Add($"{adr.RelativePath}:{i + 1} -> {lines[i].Trim()}");
            }
        }

        var index = AdrCorpus.IndexPath(options);
        if (File.Exists(index))
        {
            inspected++;
            var lines = File.ReadAllText(index).Replace("\r\n", "\n").Split('\n');
            var relative = AdrCorpus.Relative(index, options);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(EmDash))
                    offenders.Add($"{relative}:{i + 1} -> {lines[i].Trim()}");
            }
        }

        return new GuardResult(
            "hygiene/no-em-dashes",
            offenders,
            inspected,
            "No em-dashes (U+2014). Use a comma, a period, or parentheses; in code and CLI "
                + "examples use a regular hyphen. They leak in from generated text and from "
                + "autocorrect, and the rest of the Trax docs are held to the same rule."
        );
    }

    public static GuardResult TitlePresent(IReadOnlyList<Adr> adrs)
    {
        var offenders = adrs.Where(a => string.IsNullOrWhiteSpace(a.Title))
            .Select(a => $"{a.RelativePath}: no '# ' title heading")
            .ToList();

        return new GuardResult(
            "hygiene/title",
            offenders,
            adrs.Count,
            "Every ADR opens with a '# ' heading stating the decision, which is also what the "
                + "index row must say. Write the title as the decision itself, not as a topic: "
                + "'Schema changes are hand-written SQL', not 'Migrations'."
        );
    }
}
