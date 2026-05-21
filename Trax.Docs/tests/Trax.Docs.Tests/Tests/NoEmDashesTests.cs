namespace Trax.Docs.Tests.Tests;

[TestFixture]
public class NoEmDashesTests
{
    [Test]
    public void DocsMarkdown_ContainsNo_EmDashes()
    {
        var offenders = new List<string>();

        foreach (var file in RepoRoot.MarkdownFiles())
        {
            var lines = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains('—'))
                    offenders.Add($"{RepoRoot.Relative(file)}:{i + 1}  -> {lines[i].Trim()}");
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "CLAUDE.md > Voice and Tone forbids em-dashes (U+2014, the long dash). "
                    + "Use a comma, period, or parentheses; in code/CLI examples use a regular hyphen. "
                    + "Em-dashes leak in from AI-generated text and from autocorrect; CI catches them "
                    + "before the docs ship to traxsharp.net. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }
}
