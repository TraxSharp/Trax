namespace Trax.Adr.Guard.Tests.Fixtures;

/// <summary>One ADR's identity, as both the document and the index row need to agree on it.</summary>
public sealed record AdrSpec(
    string Number,
    string Slug,
    string Title,
    string[] Areas,
    string[] Repos
)
{
    public string FileName => $"{Number}-{Slug}.md";

    public string Link => $"[{Number}](./{FileName})";
}

/// <summary>
/// Canonical well-formed ADR and index text. Tests override one argument at a time, so
/// each red case differs from the green baseline in exactly the way its name says.
/// </summary>
public static class Sample
{
    public static readonly AdrSpec DefaultSpec = new(
        "0001",
        "tests-are-deterministic",
        "Tests are deterministic",
        ["testing"],
        ["effect"]
    );

    /// <summary>Long enough to clear the minimum, and not phrased as a deferral.</summary>
    public const string GoodReason =
        "a rule about how rules are written cannot check itself, and nothing here compiles "
        + "without it, so there is no state of the repo where it is violated.";

    public static string Adr(
        AdrSpec? spec = null,
        bool central = false,
        string? authors = "[Theauxm]",
        string? areas = null,
        string? repos = null,
        string status = "accepted",
        string? extraFrontmatter = null,
        string? title = null,
        string statusSection = "**Accepted.**",
        string? exemplars = null,
        string? changelog = "- **2026-09-11**: Recorded."
    )
    {
        spec ??= DefaultSpec;
        areas ??= Flow(spec.Areas);
        repos ??= central ? Flow(spec.Repos) : null;

        var lines = new List<string> { "---" };
        if (authors is not null)
            lines.Add($"authors: {authors}");
        if (repos is not null)
            lines.Add($"repos: {repos}");
        lines.Add($"areas: {areas}");
        lines.Add($"status: {status}");
        if (extraFrontmatter is not null)
            lines.Add(extraFrontmatter);
        lines.Add("---");

        var body = $"""

            # {title ?? spec.Title}

            One paragraph saying what the context is and what was decided.

            ## Status

            {statusSection}

            ## Exemplars

            {exemplars ?? $"**Unenforced:** {GoodReason}"}

            ## Changelog

            {changelog}
            """;

        return string.Join('\n', lines) + body + "\n";
    }

    public static string Index(bool central, params AdrSpec[] specs)
    {
        var text = new List<string> { "# Decisions", "", "Why a thing is the way it is.", "" };

        if (central)
        {
            text.Add("## By repo");
            text.Add("");
            text.Add("| Repo | ADRs |");
            text.Add("| --- | --- |");
            foreach (var (repo, entries) in Group(specs, s => s.Repos))
                text.Add($"| `{repo}` | {entries} |");
            text.Add("");
        }

        text.Add("## By area");
        text.Add("");
        text.Add("| Area | ADRs |");
        text.Add("| --- | --- |");
        foreach (var (area, entries) in Group(specs, s => s.Areas))
            text.Add($"| `{area}` | {entries} |");
        text.Add("");

        text.Add("## All of them");
        text.Add("");
        text.Add(central ? "| # | Decision | Repos | Areas |" : "| # | Decision | Areas |");
        text.Add(central ? "| --- | --- | --- | --- |" : "| --- | --- | --- |");
        foreach (var spec in specs)
        {
            text.Add(
                central
                    ? $"| {spec.Link} | {spec.Title} | {string.Join(", ", spec.Repos)} | {string.Join(", ", spec.Areas)} |"
                    : $"| {spec.Link} | {spec.Title} | {string.Join(", ", spec.Areas)} |"
            );
        }

        return string.Join('\n', text) + "\n";
    }

    private static IEnumerable<(string Tag, string Entries)> Group(
        IEnumerable<AdrSpec> specs,
        Func<AdrSpec, string[]> tags
    ) =>
        specs
            .SelectMany(s => tags(s).Select(t => (Tag: t, Spec: s)))
            .GroupBy(x => x.Tag, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, string.Join(", ", g.Select(x => x.Spec.Link))));

    private static string Flow(IEnumerable<string> values) => $"[{string.Join(", ", values)}]";
}
