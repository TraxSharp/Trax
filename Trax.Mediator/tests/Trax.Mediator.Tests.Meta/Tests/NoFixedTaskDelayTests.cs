namespace Trax.Mediator.Tests.Meta.Tests;

[TestFixture]
public class NoFixedTaskDelayTests
{
    private static readonly Regex DelayCall = new(
        @"\b(Task\.Delay|Thread\.Sleep)\s*\(",
        RegexOptions.Compiled
    );

    private static readonly Regex Justification = new(
        @"(?i)(determinism:|allowed-delay:|measuring-interval:|negative-wait:)",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Pre-existing offenders that pre-date the determinism convention. Each entry is a
    /// repo-relative path with its current offender count. New code MUST NOT add fixed-duration
    /// Task.Delay / Thread.Sleep to these files. To remove an entry: refactor the test to
    /// synchronise on the completion signal (TaskCompletionSource, polling) as in CLAUDE.md >
    /// Determinism, then delete the entry.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> BaselineOffenders = new Dictionary<
        string,
        int
    >(StringComparer.Ordinal)
    {
        ["tests/Trax.Mediator.Tests.MemoryLeak.Integration/UnitTests/ConcurrencyLimiterTests.cs"] =
            13,
        [
            "tests/Trax.Mediator.Tests.MemoryLeak.Integration/UnitTests/TrustedExecutionScopeTests.cs"
        ] = 3,
        [
            "tests/Trax.Mediator.Tests.MemoryLeak.Integration/IntegrationTests/TrainExecutionServiceTests.cs"
        ] = 3,
        [
            "tests/Trax.Mediator.Tests.MemoryLeak.Integration/IntegrationTests/ParameterEffectMemoryTests.cs"
        ] = 2,
        [
            "tests/Trax.Mediator.Tests.MemoryLeak.Integration/IntegrationTests/JsonEffectProviderMemoryTests.cs"
        ] = 2,
        ["tests/Trax.Mediator.Tests.MemoryLeak.Integration/Fakes/Trains/MemoryTestTrain.cs"] = 2,
        [
            "tests/Trax.Mediator.Tests.Postgres.Integration/IntegrationTests/DataContextLoggingProviderTests.cs"
        ] = 1,
        ["tests/Trax.Mediator.Tests.Postgres.Integration/IntegrationTests/BackgroundJobTests.cs"] =
            1,
        ["tests/Trax.Mediator.Tests.MemoryLeak.Integration/Utils/MemoryProfiler.cs"] = 1,
        [
            "tests/Trax.Mediator.Tests.MemoryLeak.Integration/UnitTests/MediatorServiceExtensionsTests.cs"
        ] = 1,
        ["tests/Trax.Mediator.Tests.MemoryLeak.Integration/IntegrationTests/RunExecutorTests.cs"] =
            1,
        [
            "tests/Trax.Mediator.Tests.MemoryLeak.Integration/IntegrationTests/MetadataDisposalTests.cs"
        ] = 1,
        [
            "tests/Trax.Mediator.Tests.MemoryLeak.Integration/IntegrationTests/DataContextLoggerMemoryTests.cs"
        ] = 1,
        [
            "tests/Trax.Mediator.Tests.MemoryLeak.Integration/IntegrationTests/CrossComponentMemoryTests.cs"
        ] = 1,
        [
            "tests/Trax.Mediator.Tests.MemoryLeak.Integration/IntegrationTests/CoreTrainMemoryTests.cs"
        ] = 1,
        [
            "tests/Trax.Mediator.Tests.MemoryLeak.Integration/IntegrationTests/ArrayLoggerMemoryTests.cs"
        ] = 1,
    };

    [Test]
    public void TestSources_DoNotIntroduce_NewFixedDelays()
    {
        var newOffenders = new List<string>();
        var fileOffenderCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in SourceFiles.CSharp("tests"))
        {
            if (file.EndsWith("NoFixedTaskDelayTests.cs", StringComparison.Ordinal))
                continue;

            var raw = File.ReadAllText(file);
            var lines = raw.Replace("\r\n", "\n").Split('\n');
            var stripped = SourceText.StripCommentsAndStrings(raw);
            var strippedLines = stripped.Replace("\r\n", "\n").Split('\n');

            var rel = RepoRoot.Relative(file).Replace('\\', '/');
            var count = 0;

            for (var i = 0; i < strippedLines.Length; i++)
            {
                if (!DelayCall.IsMatch(strippedLines[i]))
                    continue;
                if (HasJustification(lines, i))
                    continue;

                count++;
                if (!BaselineOffenders.ContainsKey(rel))
                    newOffenders.Add($"{rel}:{i + 1}  -> {lines[i].Trim()}");
            }

            if (count > 0)
                fileOffenderCounts[rel] = count;
        }

        newOffenders
            .Should()
            .BeEmpty(
                "CLAUDE.md > Determinism forbids fixed-duration Task.Delay / Thread.Sleep in tests. "
                    + "Synchronise on the completion signal (TaskCompletionSource, polling) with a "
                    + "generous timeout. If a fixed delay is legitimately required, add a justification "
                    + "comment containing 'determinism:', 'allowed-delay:', 'measuring-interval:', or "
                    + "'negative-wait:' on the same line or up to 3 lines above. New offenders:\n  "
                    + string.Join("\n  ", newOffenders)
            );

        var regressions = new List<string>();
        foreach (var (path, baselineCount) in BaselineOffenders)
        {
            var actual = fileOffenderCounts.TryGetValue(path, out var c) ? c : 0;
            if (actual > baselineCount)
                regressions.Add(
                    $"{path}: baseline={baselineCount}, actual={actual} (+{actual - baselineCount})"
                );
        }

        regressions
            .Should()
            .BeEmpty(
                "A grandfathered file gained new fixed-delay offenders. Either refactor the new code "
                    + "to use proper synchronisation, or update the BaselineOffenders count in "
                    + "NoFixedTaskDelayTests (but prefer refactoring). Regressions:\n  "
                    + string.Join("\n  ", regressions)
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
