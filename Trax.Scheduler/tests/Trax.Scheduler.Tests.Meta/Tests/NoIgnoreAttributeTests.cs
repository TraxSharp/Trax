namespace Trax.Scheduler.Tests.Meta.Tests;

[TestFixture]
public class NoIgnoreAttributeTests
{
    private static readonly Regex IgnoreAttribute = new(
        @"\[\s*Ignore(\s*\(|\s*\])",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Files where [Ignore] is explicitly accepted. Each entry must justify why.
    /// </summary>
    private static readonly HashSet<string> KnownExceptions = new(StringComparer.Ordinal)
    {
        // Stress tests are opt-in and must be invoked via
        // `dotnet test --filter TestCategory=Stress`. They are intentionally [Ignore]'d
        // by default so they don't run on every CI build (they take 10+ minutes and
        // generate heavy load that interferes with parallel test runs).
        "tests/Trax.Scheduler.Tests.Stress/Fixtures/TestSetup.cs",
    };

    [Test]
    public void TestSources_DoNotUse_IgnoreAttribute()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles.CSharp("tests"))
        {
            if (file.EndsWith("NoIgnoreAttributeTests.cs", StringComparison.Ordinal))
                continue;

            var rel = RepoRoot.Relative(file).Replace('\\', '/');
            if (KnownExceptions.Contains(rel))
                continue;

            var content = File.ReadAllText(file);
            var stripped = SourceText.StripCommentsAndStrings(content);
            var hits = SourceText.MatchingLines(stripped, IgnoreAttribute);
            foreach (var (line, _) in hits)
                offenders.Add($"{rel}:{line}");
        }

        offenders
            .Should()
            .BeEmpty(
                "[Ignore] silently hides failing tests. CLAUDE.md > No [Ignore] requires either "
                    + "fixing the underlying code, fixing the test premise, or using Assert.Ignore(\"reason\") "
                    + "at runtime with an explicit reachability check. If a file legitimately needs to be "
                    + "opt-in via [Ignore] (e.g. stress tests gated on a TestCategory filter), add it to "
                    + "KnownExceptions with a justification. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }
}
