namespace Trax.Samples.Tests.Meta.Tests;

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
        // GameServer group-fair-batching tests depend on Trax.Scheduler#44 which is not yet
        // merged. The placeholder tests are intentionally [Ignore]'d until the feature lands.
        "tests/Trax.Samples.GameServer.E2E/SchedulerTests/GroupFairBatchingE2ETests.cs",
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
                    + "opt-in via [Ignore] (e.g. placeholder tests gated on an upstream feature), add it to "
                    + "KnownExceptions with a justification. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }
}
