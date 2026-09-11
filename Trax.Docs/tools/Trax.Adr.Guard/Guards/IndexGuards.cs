namespace Trax.Adr.Guard.Guards;

/// <summary>
/// The index is checked against the frontmatter in BOTH directions, so neither can drift.
/// An index nothing verifies is the failure mode this whole corpus exists to avoid: it
/// looks authoritative and quietly stops being true.
/// </summary>
public static class IndexGuards
{
    public const string ByRepoHeading = "By repo";
    public const string ByAreaHeading = "By area";
    public const string FullListHeading = "All of them";

    private static readonly Regex RowLink = new(
        @"\[[^\]]*\]\(\.\/(?<file>\d{4}-[a-z0-9-]+\.md)\)",
        RegexOptions.Compiled
    );

    public static GuardResult Exists(IReadOnlyList<Adr> adrs, GuardOptions options)
    {
        var path = AdrCorpus.IndexPath(options);
        var offenders = new List<string>();

        if (adrs.Count > 0 && !File.Exists(path))
            offenders.Add(
                $"{AdrCorpus.Relative(path, options)}: the corpus has {adrs.Count} ADR(s) but no index"
            );

        return new GuardResult(
            "index/exists",
            offenders,
            adrs.Count,
            "The ADR directory carries a README.md indexing every ADR. A new ADR is not finished "
                + "when the file is written; it is finished when the index lists it."
        );
    }

    /// <summary>Every ADR appears in the tag table under each tag it declares, and nowhere else.</summary>
    public static GuardResult TagTable(
        IReadOnlyList<Adr> adrs,
        GuardOptions options,
        string heading,
        string frontmatterKey
    )
    {
        var index = ReadIndex(options);
        var offenders = new List<string>();
        var name = $"index/{frontmatterKey}-table";

        if (index is null)
            return new GuardResult(name, [], 0, Rule(heading, frontmatterKey));

        var section = Markdown.Section(index, heading);
        if (section is null)
        {
            return new GuardResult(
                name,
                [$"the index has no '## {heading}' section"],
                adrs.Count,
                Rule(heading, frontmatterKey)
            );
        }

        var listed = ParseTagRows(section);

        // Frontmatter -> index.
        foreach (var adr in adrs)
        {
            foreach (var tag in adr.Frontmatter.List(frontmatterKey) ?? [])
            {
                if (!listed.TryGetValue(tag, out var files) || !files.Contains(adr.FileName))
                    offenders.Add(
                        $"'## {heading}': {adr.FileName} declares {frontmatterKey} '{tag}' but the "
                            + $"'{tag}' row does not list it"
                    );
            }
        }

        // Index -> frontmatter.
        var byFile = adrs.ToDictionary(a => a.FileName, a => a, StringComparer.Ordinal);
        foreach (var (tag, files) in listed)
        {
            foreach (var file in files)
            {
                if (!byFile.TryGetValue(file, out var adr))
                {
                    offenders.Add(
                        $"'## {heading}': the '{tag}' row lists {file}, which does not exist"
                    );
                    continue;
                }

                var declared = adr.Frontmatter.List(frontmatterKey) ?? [];
                if (!declared.Contains(tag, StringComparer.Ordinal))
                    offenders.Add(
                        $"'## {heading}': the '{tag}' row lists {file}, which does not declare "
                            + $"{frontmatterKey} '{tag}'"
                    );
            }
        }

        return new GuardResult(name, offenders, adrs.Count, Rule(heading, frontmatterKey));
    }

    /// <summary>The full table lists every ADR exactly once, with its title and its tags.</summary>
    public static GuardResult FullList(IReadOnlyList<Adr> adrs, GuardOptions options)
    {
        var index = ReadIndex(options);
        var offenders = new List<string>();
        const string name = "index/full-list";
        var rule =
            $"The '## {FullListHeading}' table lists every ADR with its title and its tags, so the "
            + "index is a complete picture rather than a partial one.";

        if (index is null)
            return new GuardResult(name, [], 0, rule);

        var section = Markdown.Section(index, FullListHeading);
        if (section is null)
            return new GuardResult(
                name,
                [$"the index has no '## {FullListHeading}' section"],
                adrs.Count,
                rule
            );

        var rows = ParseFullRows(section, options.RequireReposKey);

        foreach (var adr in adrs)
        {
            if (!rows.TryGetValue(adr.FileName, out var row))
            {
                offenders.Add($"'## {FullListHeading}': {adr.FileName} has no row");
                continue;
            }

            if (
                adr.Title is not null
                && !string.Equals(row.Title, adr.Title, StringComparison.Ordinal)
            )
                offenders.Add(
                    $"'## {FullListHeading}': the row for {adr.FileName} says '{row.Title}' but the "
                        + $"document's title is '{adr.Title}'"
                );

            if (options.RequireReposKey)
                CompareTags(offenders, adr, "repos", row.Repos);

            CompareTags(offenders, adr, "areas", row.Areas);
        }

        var known = adrs.Select(a => a.FileName).ToHashSet(StringComparer.Ordinal);
        foreach (var file in rows.Keys.Where(f => !known.Contains(f)))
            offenders.Add($"'## {FullListHeading}': lists {file}, which does not exist");

        return new GuardResult(name, offenders, adrs.Count, rule);
    }

    private static string Rule(string heading, string key) =>
        $"The '## {heading}' table is checked against every ADR's '{key}' frontmatter in both "
        + "directions: an ADR missing from its row fails, and a row naming an ADR that does not "
        + "declare that tag fails too.";

    private static string? ReadIndex(GuardOptions options)
    {
        var path = AdrCorpus.IndexPath(options);
        return File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : null;
    }

    private static Dictionary<string, HashSet<string>> ParseTagRows(string section)
    {
        var rows = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var cells in Markdown.TableRows(section, minimumCells: 2))
        {
            var tag = cells[0].Trim().Trim('`');
            if (tag.Length == 0)
                continue;

            var files = RowLink.Matches(cells[1]).Select(m => m.Groups["file"].Value);
            if (!rows.TryGetValue(tag, out var set))
                rows[tag] = set = new HashSet<string>(StringComparer.Ordinal);

            foreach (var file in files)
                set.Add(file);
        }

        return rows;
    }

    /// <summary>One row of the full table: the title and the tag columns, as written.</summary>
    private sealed record FullRow(
        string Title,
        IReadOnlyList<string> Repos,
        IReadOnlyList<string> Areas
    );

    private static Dictionary<string, FullRow> ParseFullRows(string section, bool withRepos)
    {
        var rows = new Dictionary<string, FullRow>(StringComparer.Ordinal);
        var minimum = withRepos ? 4 : 3;

        foreach (var cells in Markdown.TableRows(section, minimum))
        {
            var match = RowLink.Match(cells[0]);
            if (!match.Success)
                continue;

            rows[match.Groups["file"].Value] = new FullRow(
                cells[1].Trim(),
                withRepos ? SplitTags(cells[2]) : [],
                SplitTags(withRepos ? cells[3] : cells[2])
            );
        }

        return rows;
    }

    private static List<string> SplitTags(string cell) =>
        cell.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim('`'))
            .ToList();

    /// <summary>
    /// The tag columns are not decoration: a reader scanning the full table takes them as
    /// the answer, so they are checked against the frontmatter like the tag tables are.
    /// </summary>
    private static void CompareTags(
        List<string> offenders,
        Adr adr,
        string key,
        IReadOnlyList<string> listed
    )
    {
        var declared = adr.Frontmatter.List(key) ?? [];

        var missing = declared.Except(listed, StringComparer.Ordinal).ToList();
        var extra = listed.Except(declared, StringComparer.Ordinal).ToList();

        if (missing.Count > 0)
            offenders.Add(
                $"'## {FullListHeading}': the row for {adr.FileName} omits {key} "
                    + string.Join(", ", missing)
            );

        if (extra.Count > 0)
            offenders.Add(
                $"'## {FullListHeading}': the row for {adr.FileName} lists {key} "
                    + string.Join(", ", extra)
                    + ", which it does not declare"
            );
    }
}
