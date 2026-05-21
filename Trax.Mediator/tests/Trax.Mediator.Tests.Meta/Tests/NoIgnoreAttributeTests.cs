namespace Trax.Mediator.Tests.Meta.Tests;

[TestFixture]
public class NoIgnoreAttributeTests
{
    private static readonly Regex IgnoreAttribute = new(
        @"\[\s*Ignore(\s*\(|\s*\])",
        RegexOptions.Compiled
    );

    [Test]
    public void TestSources_DoNotUse_IgnoreAttribute()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles.CSharp("tests"))
        {
            if (file.EndsWith("NoIgnoreAttributeTests.cs", StringComparison.Ordinal))
                continue;

            var content = File.ReadAllText(file);
            var stripped = SourceText.StripCommentsAndStrings(content);
            var hits = SourceText.MatchingLines(stripped, IgnoreAttribute);
            foreach (var (line, _) in hits)
                offenders.Add($"{RepoRoot.Relative(file)}:{line}");
        }

        offenders
            .Should()
            .BeEmpty(
                "[Ignore] silently hides failing tests. CLAUDE.md > No [Ignore] requires either "
                    + "fixing the underlying code, fixing the test premise, or using Assert.Ignore(\"reason\") "
                    + "at runtime with an explicit reachability check. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }
}
