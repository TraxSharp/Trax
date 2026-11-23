using System.Globalization;
using System.Text.RegularExpressions;
using Trax.Core.Testing.Infrastructure;

namespace Trax.Core.Testing.Guards;

/// <summary>
/// Test-hygiene guard checkers. Each scans the configured test roots and returns a
/// <see cref="GuardResult"/>; the consumer asserts <c>Offenders</c> is empty with its own framework.
/// </summary>
public static partial class HygieneGuards
{
    // A per-case skip: [TestCase(1, Ignore = "...")] or IgnoreReason = on a case or fixture
    // attribute. Same effect as [Ignore], one case at a time.
    [GeneratedRegex(
        @"\[[^\]]*?\b(?:TestCase|TestCaseSource|TestFixture|TestFixtureSource)(?:Attribute)?\b[^\]]*?\b(?:Ignore|IgnoreReason)\s*=(?!=)",
        RegexOptions.Compiled,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex IgnoreNamedArgument { get; }

    private static readonly (string Name, Regex Pattern)[] LegacyAssertPatterns =
    [
        ("Assert.That", LegacyAssertThat),
        ("Assert.AreEqual", LegacyAssertAreEqual),
        ("Assert.AreNotEqual", LegacyAssertAreNotEqual),
        ("Assert.AreSame", LegacyAssertAreSame),
        ("Assert.AreNotSame", LegacyAssertAreNotSame),
        ("Assert.IsTrue", LegacyAssertIsTrue),
        ("Assert.IsFalse", LegacyAssertIsFalse),
        ("Assert.IsNull", LegacyAssertIsNull),
        ("Assert.IsNotNull", LegacyAssertIsNotNull),
        ("Assert.IsEmpty", LegacyAssertIsEmpty),
        ("Assert.IsNotEmpty", LegacyAssertIsNotEmpty),
        ("Assert.Contains", LegacyAssertContains),
        ("ClassicAssert", LegacyClassicAssert),
        ("CollectionAssert", LegacyCollectionAssert),
        ("StringAssert", LegacyStringAssert),
    ];

    [GeneratedRegex(@"\bAssert\.That\b", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyAssertThat { get; }

    [GeneratedRegex(@"\bAssert\.AreEqual\b", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyAssertAreEqual { get; }

    [GeneratedRegex(@"\bAssert\.AreNotEqual\b", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyAssertAreNotEqual { get; }

    [GeneratedRegex(@"\bAssert\.AreSame\b", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyAssertAreSame { get; }

    [GeneratedRegex(@"\bAssert\.AreNotSame\b", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyAssertAreNotSame { get; }

    [GeneratedRegex(@"\bAssert\.IsTrue\b", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyAssertIsTrue { get; }

    [GeneratedRegex(@"\bAssert\.IsFalse\b", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyAssertIsFalse { get; }

    [GeneratedRegex(@"\bAssert\.IsNull\b", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyAssertIsNull { get; }

    [GeneratedRegex(@"\bAssert\.IsNotNull\b", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyAssertIsNotNull { get; }

    [GeneratedRegex(@"\bAssert\.IsEmpty\b", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyAssertIsEmpty { get; }

    [GeneratedRegex(@"\bAssert\.IsNotEmpty\b", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyAssertIsNotEmpty { get; }

    [GeneratedRegex(@"\bAssert\.Contains\b", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyAssertContains { get; }

    [GeneratedRegex(@"\bClassicAssert\.\w+", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyClassicAssert { get; }

    [GeneratedRegex(@"\bCollectionAssert\.\w+", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyCollectionAssert { get; }

    [GeneratedRegex(@"\bStringAssert\.\w+", RegexOptions.None, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex LegacyStringAssert { get; }

    [GeneratedRegex(
        @"\b(Task\.Delay|Thread\.Sleep)\s*\(",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex FixedDelay { get; }

    [GeneratedRegex(@"\bStartAsync\s*\(", RegexOptions.Compiled, matchTimeoutMilliseconds: 5_000)]
    private static partial Regex StartCall { get; }

    // A method, constructor or local function header ends the previous method's scan. Statements
    // never start with an access modifier, and a local function is a fresh scope too.
    [GeneratedRegex(
        @"^\s*(?:(?:public|private|protected|internal|static|async|override|virtual|sealed)\s+)+[\w<>\[\],.?\s]+\(",
        RegexOptions.Compiled,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex MemberDeclaration { get; }

    [GeneratedRegex(
        @"\bWaitAsync\s*\(|\bWait(?:Until|For)\w*\s*\(|\bEventually\w*\s*\(|\b\w*Until\w*Async\s*\("
            + @"|\bWhenReached\s*\(|\bawait\s+[\w.!()]+\.(?:Task|Current)\b|\bTask\.When(?:All|Any)\s*\("
            + @"|\.StopAsync\s*\(|\bawait\s+[A-Za-z_]\w*\s*;",
        RegexOptions.Compiled,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex Synchronisation { get; }

    [GeneratedRegex(
        @"\.(?:Received|DidNotReceive|ReceivedWithAnyArgs|DidNotReceiveWithAnyArgs)\s*\(",
        RegexOptions.Compiled,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex ReceivedCheck { get; }

    private const string SynchronousStart = "synchronous-start:";

    private static readonly string[] DelayJustifications =
    [
        "determinism:",
        "allowed-delay:",
        "measuring-interval:",
        "negative-wait:",
    ];

    /// <summary>Flags <c>[Ignore]</c> attributes in test sources (they silently hide failures).</summary>
    public static GuardResult NoIgnoreAttribute(ArchitectureGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharpUnder(root, [.. options.TestScanRoots]))
        {
            inspected++;
            var rel = Rel(root, file);
            if (options.NoIgnoreKnownExceptions.Contains(rel))
                continue;

            var stripped = SourceText.StripCommentsAndStrings(File.ReadAllText(file));
            var lines = new SortedSet<int>();
            foreach (var pattern in new[] { IgnoreAttribute, IgnoreNamedArgument })
            foreach (Match match in pattern.Matches(stripped))
                lines.Add(stripped.Take(match.Index).Count(c => c == '\n') + 1);

            foreach (var line in lines)
                offenders.Add(string.Create(CultureInfo.InvariantCulture, $"{rel}:{line}"));
        }

        var message =
            "[Ignore] silently hides failing tests. Fix the underlying code or the test premise, or "
            + "use Assert.Ignore(\"reason\") at runtime with a reachability check. If a file must be "
            + "opt-in via [Ignore] (e.g. a placeholder gated on an upstream feature), add it to "
            + "NoIgnoreKnownExceptions with a justification. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    /// <summary>Flags classic NUnit asserts in test sources (the convention is one assertion library, exclusively).</summary>
    public static GuardResult NoLegacyAsserts(ArchitectureGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharpUnder(root, [.. options.TestScanRoots]))
        {
            inspected++;
            var rel = Rel(root, file);
            var stripped = SourceText.StripCommentsAndStrings(File.ReadAllText(file));

            foreach (var (name, pattern) in LegacyAssertPatterns)
            {
                foreach (var (line, _) in SourceText.MatchingLines(stripped, pattern))
                    offenders.Add(
                        string.Create(CultureInfo.InvariantCulture, $"{rel}:{line}  ({name})")
                    );
            }
        }

        var message =
            "Use the project's chosen assertion library exclusively. Replace classic NUnit asserts "
            + "with the fluent equivalents. Assert.Pass / Assert.Fail / Assert.Ignore remain "
            + "acceptable. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    /// <summary>
    /// Flags fixed-duration <c>Task.Delay</c> / <c>Thread.Sleep</c> in test sources unless the line
    /// (or up to three lines above) carries a justification marker, or the file is allowlisted.
    /// </summary>
    public static GuardResult NoFixedDelays(ArchitectureGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharpUnder(root, [.. options.TestScanRoots]))
        {
            inspected++;
            var rel = Rel(root, file);
            if (options.FixedDelayKnownExceptions.Contains(rel))
                continue;

            var raw = File.ReadAllText(file)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n');
            var stripped = SourceText
                .StripCommentsAndStrings(File.ReadAllText(file))
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n');

            for (var i = 0; i < stripped.Length && i < raw.Length; i++)
            {
                if (!FixedDelay.IsMatch(stripped[i]))
                    continue;
                if (HasJustification(raw, i))
                    continue;
                offenders.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{rel}:{i + 1}  -> {raw[i].Trim()}"
                    )
                );
            }
        }

        var message =
            "Fixed-duration Task.Delay / Thread.Sleep make tests flaky. Synchronise on the completion "
            + "signal (TaskCompletionSource, polling) with a generous timeout. If a fixed delay is "
            + "genuinely required, add a same-line or up-to-3-lines-above comment containing one of: "
            + string.Join(", ", DelayJustifications)
            + ". Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    private static string Rel(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');

    /// <summary>
    /// Flags a substitute's <c>Received</c> / <c>DidNotReceive</c> check that follows a
    /// <c>StartAsync</c> call in the same method with nothing in between that waits for the started
    /// work: a <c>WaitAsync</c>, a <c>WaitUntil…</c> / <c>WaitFor…</c> / <c>Eventually…</c> /
    /// <c>…Until…Async</c> helper, <c>WhenReached</c>, an awaited <c>.Task</c> or <c>.Current</c>,
    /// an awaited local (<c>await started;</c>, a signal captured before the start),
    /// <c>Task.WhenAll</c> / <c>Task.WhenAny</c>, or <c>StopAsync</c>.
    /// </summary>
    /// <remarks>
    /// <c>BackgroundService.StartAsync</c> hands <c>ExecuteAsync</c> to the thread pool, and a job
    /// started with <c>Task.Run</c> returns its handle before it runs, so a check made straight after
    /// the start races the work. A start that finishes its work before returning carries a
    /// <c>synchronous-start:</c> comment on its line or up to three lines above.
    /// </remarks>
    public static GuardResult NoUnsynchronisedAssertAfterStart(ArchitectureGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharpUnder(root, [.. options.TestScanRoots]))
        {
            inspected++;
            var rel = Rel(root, file);
            var raw = File.ReadAllText(file)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n');
            var stripped = SourceText
                .StripCommentsAndStrings(File.ReadAllText(file))
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n');

            AddUnsynchronisedChecks(rel, raw, stripped, offenders);
        }

        var message =
            "A substitute is checked straight after StartAsync, before anything waits for the work "
            + "the start handed to the background, so the check races that work. Wait for the signal "
            + "that means the work happened (a TaskCompletionSource the substitute completes, a "
            + "bounded WaitUntil, StopAsync) before checking. If the start finishes its work before "
            + "it returns, say so in a '"
            + SynchronousStart
            + "' comment on its line or up to three lines above. Offenders:\n  "
            + string.Join("\n  ", offenders);

        return new GuardResult(offenders, inspected, message);
    }

    /// <summary>
    /// Adds each line of one file that checks a substitute after a start with nothing between
    /// them that waits for the started work.
    /// </summary>
    private static void AddUnsynchronisedChecks(
        string rel,
        string[] raw,
        string[] stripped,
        List<string> offenders
    )
    {
        int? started = null;
        for (var i = 0; i < stripped.Length && i < raw.Length; i++)
        {
            var line = stripped[i];
            if (MemberDeclaration.IsMatch(line))
            {
                started = null;
                continue;
            }
            if (StartCall.IsMatch(line) && !HasMarker(raw, i, SynchronousStart))
            {
                started = i;
                continue;
            }
            if (started is null)
                continue;
            if (Synchronisation.IsMatch(line))
            {
                started = null;
                continue;
            }
            if (ReceivedCheck.IsMatch(line))
            {
                offenders.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{rel}:{i + 1}  -> {raw[i].Trim()}  (started at line {started.Value + 1})"
                    )
                );
                started = null;
            }
        }
    }

    private static bool HasMarker(string[] rawLines, int lineIndex, string marker)
    {
        var start = Math.Max(0, lineIndex - 3);
        for (var j = start; j <= lineIndex && j < rawLines.Length; j++)
        {
            if (rawLines[j].Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool HasJustification(string[] rawLines, int delayLineIndex)
    {
        var start = Math.Max(0, delayLineIndex - 3);
        for (var j = start; j <= delayLineIndex && j < rawLines.Length; j++)
        {
            var lower = rawLines[j].ToLowerInvariant();
            foreach (var marker in DelayJustifications)
            {
                if (lower.Contains(marker, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    [GeneratedRegex(
        @"(?:\[|,)\s*(?:[\w.]+\.)?Ignore(?:Attribute)?(?=\s*(?:\(|\]|,))",
        RegexOptions.Compiled,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex IgnoreAttribute { get; }
}
