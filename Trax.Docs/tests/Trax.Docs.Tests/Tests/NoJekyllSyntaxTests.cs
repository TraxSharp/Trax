namespace Trax.Docs.Tests.Tests;

[TestFixture]
public class NoJekyllSyntaxTests
{
    private static readonly (string Description, Regex Pattern)[] BannedPatterns = new[]
    {
        (
            "{{ site.baseurl }} (Jekyll baseurl template)",
            new Regex(@"\{\{\s*site\.baseurl\s*\}\}", RegexOptions.Compiled)
        ),
        (
            "{% link ... %} (Jekyll link tag)",
            new Regex(@"\{%\s*link\s+", RegexOptions.Compiled)
        ),
        (
            "{% include ... %} (Jekyll include tag)",
            new Regex(@"\{%\s*include\s+", RegexOptions.Compiled)
        ),
        (
            "{: .classname } (kramdown IAL)",
            new Regex(@"\{:\s*\.[A-Za-z0-9_-]+\s*\}", RegexOptions.Compiled)
        ),
    };

    [Test]
    public void DocsMarkdown_ContainsNo_JekyllOrKramdownSyntax()
    {
        var offenders = new List<string>();

        foreach (var file in RepoRoot.MarkdownFiles())
        {
            var rel = RepoRoot.Relative(file).Replace('\\', '/');
            // Self-reference exclusion: the lint test source mentions these patterns in strings.
            if (rel.EndsWith("NoJekyllSyntaxTests.cs", StringComparison.Ordinal))
                continue;

            var lines = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (desc, pattern) in BannedPatterns)
                {
                    if (pattern.IsMatch(lines[i]))
                        offenders.Add(
                            $"{rel}:{i + 1}  -> {desc}: {lines[i].Trim()}"
                        );
                }
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "CLAUDE.md > Documentation Link Format requires plain Markdown — the docs are rendered "
                    + "by Trax.Website (Next.js/React), not Jekyll. Use direct `/docs/path/to/file` "
                    + "links instead of `{{ site.baseurl }}{% link %}` or `{: .classname }` IALs. "
                    + "Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }
}
