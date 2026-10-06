namespace Trax.Adr.Guard.Guards;

/// <summary>
/// The frontmatter contract: the fields that are read by machines. Everything else about
/// a decision stays prose.
/// </summary>
public static class FrontmatterGuards
{
    /// <summary>The four shapes <c>status</c> may take. <c>superseded-by</c> carries the number that replaced it.</summary>
    public static readonly Regex StatusPattern = new(
        @"^(proposed|accepted|deprecated|superseded-by-\d{4})$",
        RegexOptions.Compiled
    );

    public static GuardResult Parseable(IReadOnlyList<Adr> adrs) =>
        Check(
            adrs,
            "frontmatter/parseable",
            adr => adr.Frontmatter.Parsed ? null : adr.Frontmatter.Error,
            "Every ADR opens with a '---' delimited frontmatter block using the accepted YAML "
                + "subset: 'key: value' scalars, flow lists ([a, b]) and block lists ('- a' on "
                + "following lines). Nothing else is accepted, because the subset is the contract."
        );

    public static GuardResult Authors(IReadOnlyList<Adr> adrs) =>
        Check(
            adrs,
            "frontmatter/authors",
            adr =>
            {
                if (!adr.Frontmatter.Parsed)
                    return null;

                var authors = adr.Frontmatter.List("authors");
                if (authors is null)
                    return adr.Frontmatter.Has("authors")
                        ? "'authors' must be a list, not a scalar"
                        : "'authors' is missing";
                if (authors.Count == 0)
                    return "'authors' is empty";

                var at = authors.Where(a => a.StartsWith('@')).ToList();
                return at.Count > 0
                    ? $"'authors' entries must not carry '@': {string.Join(", ", at)}"
                    : null;
            },
            "'authors' lists the humans whose decision this is, as GitHub usernames without "
                + "'@'. Not whoever typed it: when an agent writes an ADR the authors are the "
                + "people it is working with, and an agent never lists itself."
        );

    public static GuardResult Areas(IReadOnlyList<Adr> adrs, GuardOptions options) =>
        Check(
            adrs,
            "frontmatter/areas",
            adr =>
            {
                if (!adr.Frontmatter.Parsed)
                    return null;

                var areas = adr.Frontmatter.List("areas");
                if (areas is null)
                    return adr.Frontmatter.Has("areas")
                        ? "'areas' must be a list, not a scalar"
                        : "'areas' is missing";
                if (areas.Count == 0)
                    return "'areas' is empty";

                var unknown = areas.Where(a => !options.KnownAreas.Contains(a)).ToList();
                return unknown.Count > 0
                    ? $"unknown area(s) {string.Join(", ", unknown)}; known: {Vocabulary(options.KnownAreas)}"
                    : null;
            },
            "'areas' says what the ADR is about, drawn from a closed vocabulary. Adding a new "
                + "area is a deliberate edit to the --known-areas list, because a vocabulary that "
                + "grows freely stops discriminating."
        );

    public static GuardResult Repos(IReadOnlyList<Adr> adrs, GuardOptions options) =>
        Check(
            adrs,
            "frontmatter/repos",
            adr =>
            {
                if (!adr.Frontmatter.Parsed)
                    return null;

                var repos = adr.Frontmatter.List("repos");

                if (!options.RequireReposKey)
                    return adr.Frontmatter.Has("repos")
                        ? "'repos' is forbidden in a repo-local ADR: the path already says which "
                            + "repo governs it, and restating it is a second place to drift"
                        : null;

                if (repos is null)
                    return adr.Frontmatter.Has("repos")
                        ? "'repos' must be a list, not a scalar"
                        : "'repos' is missing, and a central ADR must say which repos must obey it";
                if (repos.Count == 0)
                    return "'repos' is empty";

                var unknown = repos.Where(r => !options.KnownRepos.Contains(r)).ToList();
                return unknown.Count > 0
                    ? $"unknown repo(s) {string.Join(", ", unknown)}; known: {Vocabulary(options.KnownRepos)}"
                    : null;
            },
            "'repos' names the repos whose developers must obey the decision, and is required "
                + "in the central corpus only. The test is whether someone working there could "
                + "violate it without realising. Under-list it and they never see it; over-list "
                + "it and it stops meaning anything."
        );

    public static GuardResult Status(IReadOnlyList<Adr> adrs) =>
        Check(
            adrs,
            "frontmatter/status",
            adr =>
            {
                if (!adr.Frontmatter.Parsed)
                    return null;

                var status = adr.Frontmatter.Scalar("status");
                if (status is null)
                    return adr.Frontmatter.Has("status")
                        ? "'status' must be a scalar"
                        : "'status' is missing";

                return StatusPattern.IsMatch(status)
                    ? null
                    : $"'{status}' is not one of proposed / accepted / deprecated / superseded-by-NNNN";
            },
            "'status' is what tooling reads. The '## Status' section is what a human reads "
                + "first, and the two are checked against each other."
        );

    /// <summary>The complete accepted key set. Anything else is a typo or an invention.</summary>
    private static readonly string[] AcceptedKeys = ["authors", "repos", "areas", "status"];

    public static GuardResult NoDateKey(IReadOnlyList<Adr> adrs) =>
        Check(
            adrs,
            "frontmatter/keys",
            adr =>
            {
                if (!adr.Frontmatter.Parsed)
                    return null;

                // Casing is not a loophole: 'Date' was accepted while 'date' was rejected.
                var unexpected = adr
                    .Frontmatter.Keys.Where(k =>
                        !AcceptedKeys.Contains(k, StringComparer.OrdinalIgnoreCase)
                    )
                    .ToList();

                if (unexpected.Contains("date", StringComparer.OrdinalIgnoreCase))
                    return "'date' is not an accepted key: git records when the file was written, "
                        + "and the '## Changelog' section says what changed as well as when";

                return unexpected.Count > 0
                    ? $"unexpected key(s) {string.Join(", ", unexpected)}; accepted: "
                        + string.Join(", ", AcceptedKeys)
                    : null;
            },
            "Do not add a 'date' key. Git already records when the file was written, and a "
                + "single hand-written date claims to be the whole story the first time the ADR "
                + "is amended. The '## Changelog' section is where dates belong, because it says "
                + "what changed as well as when."
        );

    public static GuardResult FileNames(IReadOnlyList<Adr> adrs)
    {
        var offenders = new List<string>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var adr in adrs)
        {
            var match = AdrCorpus.FileNamePattern.Match(adr.FileName);
            if (!match.Success)
            {
                offenders.Add($"{adr.RelativePath}: file name must be NNNN-kebab-case-slug.md");
                continue;
            }

            var number = match.Groups["number"].Value;
            if (seen.TryGetValue(number, out var other))
                offenders.Add($"{adr.RelativePath}: number {number} is already used by {other}");
            else
                seen[number] = adr.FileName;
        }

        return new GuardResult(
            "frontmatter/file-names",
            offenders,
            adrs.Count,
            "An ADR is named NNNN-slug.md, and the number is unique within its directory: it "
                + "is what the index, a supersession link and a code comment all cite. Numbering "
                + "is per directory, so the same number in another repo is fine and expected."
        );
    }

    private static string Vocabulary(IReadOnlySet<string> values) =>
        values.Count == 0
            ? "(none configured)"
            : string.Join(", ", values.OrderBy(v => v, StringComparer.Ordinal));

    private static GuardResult Check(
        IReadOnlyList<Adr> adrs,
        string name,
        Func<Adr, string?> inspect,
        string rule
    )
    {
        var offenders = new List<string>();
        foreach (var adr in adrs)
        {
            var problem = inspect(adr);
            if (problem is not null)
                offenders.Add($"{adr.RelativePath}: {problem}");
        }

        return new GuardResult(name, offenders, adrs.Count, rule);
    }
}
