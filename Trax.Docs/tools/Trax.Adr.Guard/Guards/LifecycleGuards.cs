namespace Trax.Adr.Guard.Guards;

/// <summary>
/// Where a decision stands, and how it got there. The frontmatter <c>status</c> is what
/// tooling reads; the <c>## Status</c> section is what a human reads first, and it is the
/// only place a supersession has room to be explained.
/// </summary>
public static class LifecycleGuards
{
    public const string StatusHeading = "Status";
    public const string ChangelogHeading = "Changelog";

    private static readonly Regex MarkdownLink = new(
        @"\[[^\]]*\]\(\.\/(?<file>\d{4}-[a-z0-9-]+\.md)\)",
        RegexOptions.Compiled
    );

    private static readonly Regex ChangelogEntry = new(
        @"^-\s+\*\*(?<date>\d{4}-\d{2}-\d{2})\*\*:\s*\S",
        RegexOptions.Compiled
    );

    public static GuardResult StatusSection(IReadOnlyList<Adr> adrs)
    {
        var offenders = new List<string>();

        foreach (var adr in adrs)
        {
            var section = adr.Section(StatusHeading);
            if (section is null)
            {
                offenders.Add($"{adr.RelativePath}: no '## {StatusHeading}' section");
                continue;
            }

            var declared = adr.Frontmatter.Scalar("status");
            if (declared is null)
                continue; // the frontmatter check owns this failure

            var expectedOpening = declared.StartsWith("superseded-by-", StringComparison.Ordinal)
                ? "**Superseded by"
                : $"**{char.ToUpperInvariant(declared[0])}{declared[1..]}.**";

            if (!section.StartsWith(expectedOpening, StringComparison.Ordinal))
            {
                offenders.Add(
                    $"{adr.RelativePath}: frontmatter says '{declared}', so the '## {StatusHeading}' "
                        + $"section must open with '{expectedOpening}' but opens with "
                        + $"'{FirstLine(section)}'"
                );
            }
        }

        return new GuardResult(
            "lifecycle/status-section",
            offenders,
            adrs.Count,
            "The '## Status' section opens with the bolded status and nothing in front of it: "
                + "'**Proposed.**', '**Accepted.**', '**Deprecated.**', or '**Superseded by** "
                + "[NNNN](./NNNN-slug.md)'. It is checked against the frontmatter so the two "
                + "cannot drift apart."
        );
    }

    /// <summary>
    /// A supersession is two edits or it is a lie. The superseding ADR writes
    /// <c>Supersedes</c> and links what it replaced; the superseded one sets
    /// <c>status: superseded-by-NNNN</c> and links forward. A reader arriving from a code
    /// comment lands on the OLD document, where a forward link they cannot see does them
    /// no good.
    /// </summary>
    public static GuardResult Supersessions(IReadOnlyList<Adr> adrs)
    {
        var offenders = new List<string>();
        var byNumber = adrs.Where(a => AdrCorpus.FileNamePattern.IsMatch(a.FileName))
            .ToDictionary(a => a.Number, a => a, StringComparer.Ordinal);
        var inspected = 0;

        foreach (var adr in adrs)
        {
            var declared = adr.Frontmatter.Scalar("status");
            if (
                declared is null
                || !declared.StartsWith("superseded-by-", StringComparison.Ordinal)
            )
                continue;

            inspected++;
            var targetNumber = declared["superseded-by-".Length..];
            var section = adr.Section(StatusHeading) ?? string.Empty;

            if (!byNumber.TryGetValue(targetNumber, out var target))
            {
                offenders.Add(
                    $"{adr.RelativePath}: status names {targetNumber}, but no ADR with that number "
                        + "exists in this directory"
                );
                continue;
            }

            var forwardLinks = MarkdownLink
                .Matches(section)
                .Select(m => m.Groups["file"].Value)
                .ToList();

            if (!forwardLinks.Contains(target.FileName, StringComparer.Ordinal))
            {
                offenders.Add(
                    $"{adr.RelativePath}: '## {StatusHeading}' must link forward to "
                        + $"[{targetNumber}](./{target.FileName})"
                );
            }

            var targetSection = target.Section(StatusHeading) ?? string.Empty;
            var backLinks = MarkdownLink
                .Matches(targetSection)
                .Select(m => m.Groups["file"].Value)
                .ToList();

            if (
                !targetSection.Contains("Supersedes", StringComparison.Ordinal)
                || !backLinks.Contains(adr.FileName, StringComparer.Ordinal)
            )
            {
                offenders.Add(
                    $"{target.RelativePath}: supersedes {adr.FileName} but does not say so. Its "
                        + $"'## {StatusHeading}' section must write 'Supersedes "
                        + $"[{adr.Number}](./{adr.FileName})' and say what was wrong with it"
                );
            }
        }

        return new GuardResult(
            "lifecycle/supersessions",
            offenders,
            inspected,
            "A supersession is written from both sides: the new ADR says 'Supersedes' and links "
                + "back, the old one sets status 'superseded-by-NNNN' and links forward. Half of "
                + "it is worse than none, because a reader arriving from a code comment lands on "
                + "the old document.",
            // A corpus with no supersessions genuinely has nothing to check.
            AllowsEmpty: true
        );
    }

    public static GuardResult Changelog(IReadOnlyList<Adr> adrs)
    {
        var offenders = new List<string>();

        foreach (var adr in adrs)
        {
            var section = adr.Section(ChangelogHeading);
            if (section is null)
            {
                offenders.Add($"{adr.RelativePath}: no '## {ChangelogHeading}' section");
                continue;
            }

            var dates = new List<string>();
            var malformed = new List<string>();

            foreach (var line in section.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;

                var match = ChangelogEntry.Match(trimmed);
                if (match.Success)
                    dates.Add(match.Groups["date"].Value);
                else if (trimmed.StartsWith('-'))
                    malformed.Add(trimmed);
            }

            if (malformed.Count > 0)
            {
                offenders.Add(
                    $"{adr.RelativePath}: malformed changelog entr(ies): {string.Join(" | ", malformed)}"
                );
                continue;
            }

            if (dates.Count == 0)
            {
                offenders.Add($"{adr.RelativePath}: '## {ChangelogHeading}' has no entries");
                continue;
            }

            var sorted = dates.OrderByDescending(d => d, StringComparer.Ordinal).ToList();
            if (!dates.SequenceEqual(sorted, StringComparer.Ordinal))
                offenders.Add(
                    $"{adr.RelativePath}: changelog is not newest first: {string.Join(", ", dates)}"
                );
        }

        return new GuardResult(
            "lifecycle/changelog",
            offenders,
            adrs.Count,
            "Every ADR carries a '## Changelog', newest first, one line each:\n"
                + "  - **YYYY-MM-DD**: what changed.\n"
                + "A new ADR gets one entry, '- **YYYY-MM-DD**: Recorded.'. Git has every edit and "
                + "cannot tell an amendment from a typo fix; this is the curated half, so record "
                + "substantive changes only."
        );
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length <= 60 ? line : line[..60] + "...";
    }
}
