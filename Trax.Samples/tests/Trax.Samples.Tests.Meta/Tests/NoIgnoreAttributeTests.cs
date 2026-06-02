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

    [Test]
    public void KnownExceptions_AreNotStale()
    {
        var stale = new List<string>();

        foreach (var rel in KnownExceptions)
        {
            var absolute = RepoRoot.Combine(rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                stale.Add($"{rel} (file no longer exists)");
                continue;
            }

            var stripped = SourceText.StripCommentsAndStrings(File.ReadAllText(absolute));
            if (!IgnoreAttribute.IsMatch(stripped))
                stale.Add($"{rel} (no [Ignore] left — exception no longer needed)");
        }

        stale
            .Should()
            .BeEmpty(
                "A KnownExceptions entry is stale: the file is gone or no longer uses [Ignore]. "
                    + "Remove the entry so the allowlist reflects reality:\n  "
                    + string.Join("\n  ", stale)
            );
    }
}
