namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Every published page carries a one-line <c>description</c> in its front matter.
///
/// <para>traxsharp.net uses it as the page's meta description and as the page's line in
/// <c>llms.txt</c>, which is how a search result or an agent decides whether the page is worth
/// opening. Without it the site falls back to the first prose paragraph, which on many pages is
/// a security notice, a lead-in to a code block, or a sentence cut off mid-clause.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0008-documentation-conventions-are-linted.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0008-documentation-conventions-are-linted.md")]
[TestFixture]
public class PageDescriptionTests
{
    private const string Adr = "Trax.Docs/adr/0008-documentation-conventions-are-linted.md";

    /// <summary>
    /// The length at which traxsharp.net truncates a description (<c>SUMMARY_MAX</c> in its
    /// <c>src/lib/docs.ts</c>). A longer one is cut off with an ellipsis where the reader sees it.
    /// </summary>
    private const int MaxLength = 160;

    private static readonly Regex FrontMatter = new(
        @"\A---\n(?<body>.*?)\n---\n",
        RegexOptions.Singleline
    );

    [Test]
    public void EveryPublishedPage_HasA_ShortDescription()
    {
        var offenders = new List<string>();

        foreach (var file in RepoRoot.MarkdownFiles())
        {
            var rel = RepoRoot.Relative(file).Replace('\\', '/');
            if (!RepoRoot.IsPublished(rel))
                continue;

            var problem = Check(File.ReadAllText(file).Replace("\r\n", "\n"));
            if (problem is not null)
                offenders.Add($"{rel}  -> {problem}");
        }

        offenders
            .Should()
            .BeEmpty(
                "every page traxsharp.net publishes needs a `description:` line in its front matter, "
                    + $"one plain-text sentence of at most {MaxLength} characters saying what the page "
                    + "covers. The site uses it for the meta description and for llms.txt, and falls "
                    + "back to the first paragraph without it. See reference/contributing-docs.md and "
                    + Adr
                    + ". Offenders:\n  "
                    + string.Join("\n  ", offenders.OrderBy(o => o, StringComparer.Ordinal))
            );
    }

    private static string? Check(string text)
    {
        var match = FrontMatter.Match(text);
        if (!match.Success)
            return "no front matter";

        var line = match
            .Groups["body"]
            .Value.Split('\n')
            .FirstOrDefault(l => l.StartsWith("description:", StringComparison.Ordinal));
        if (line is null)
            return "no description";

        var value = line["description:".Length..].Trim();
        if (value.StartsWith('|') || value.StartsWith('>'))
            return "description is a block scalar; write it on one line";
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            value = value[1..^1].Trim();

        if (value.Length == 0)
            return "description is empty";
        if (value.Length > MaxLength)
            return $"description is {value.Length} characters, over {MaxLength}";
        return null;
    }
}
