namespace Trax.Core.Tests.Meta.Tests;

[TestFixture]
public class NoFixedTaskDelayTests
{
    // Matches Task.Delay(...) and Thread.Sleep(...).
    private static readonly Regex DelayCall = new(
        @"\b(Task\.Delay|Thread\.Sleep)\s*\(",
        RegexOptions.Compiled
    );

    // A justification comment must appear on the same line or within the preceding 3 lines.
    // We look for any of these tokens (case-insensitive):
    //   determinism:, allowed-delay:, measuring-interval:, negative-wait:
    private static readonly Regex Justification = new(
        @"(?i)(determinism:|allowed-delay:|measuring-interval:|negative-wait:)",
        RegexOptions.Compiled
    );

    [Test]
    public void TestSources_DoNotUse_FixedDelays_WithoutJustification()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles.CSharp("tests"))
        {
            if (file.EndsWith("NoFixedTaskDelayTests.cs", StringComparison.Ordinal))
                continue;

            var raw = File.ReadAllText(file);
            var lines = raw.Replace("\r\n", "\n").Split('\n');
            var stripped = SourceText.StripCommentsAndStrings(raw);
            var strippedLines = stripped.Replace("\r\n", "\n").Split('\n');

            for (var i = 0; i < strippedLines.Length; i++)
            {
                if (!DelayCall.IsMatch(strippedLines[i]))
                    continue;

                if (HasJustification(lines, i))
                    continue;

                offenders.Add($"{RepoRoot.Relative(file)}:{i + 1}  -> {lines[i].Trim()}");
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "CLAUDE.md > Determinism forbids fixed-duration Task.Delay / Thread.Sleep in tests "
                    + "because they race CI scheduling. Synchronise on the actual completion signal "
                    + "(poll a flag, TaskCompletionSource, etc.) with a generous timeout ceiling. "
                    + "If a fixed delay is legitimately required (measuring an interval, verifying a "
                    + "negative outcome that requires a duration to elapse), add a justification comment "
                    + "containing 'determinism:', 'allowed-delay:', 'measuring-interval:', or 'negative-wait:' "
                    + "on the same line or up to 3 lines above. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    private static bool HasJustification(string[] lines, int delayLineIndex)
    {
        var from = Math.Max(0, delayLineIndex - 3);
        for (var j = from; j <= delayLineIndex; j++)
        {
            if (Justification.IsMatch(lines[j]))
                return true;
        }
        return false;
    }
}
