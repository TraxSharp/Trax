using System.Text.RegularExpressions;
using Trax.Core.Testing.Infrastructure;

namespace Trax.Core.Testing.Guards;

/// <summary>
/// Test-hygiene guard checkers. Each scans the configured test roots and returns a
/// <see cref="GuardResult"/>; the consumer asserts <c>Offenders</c> is empty with its own framework.
/// </summary>
public static class HygieneGuards
{
    // Matches an [Ignore] attribute whether standalone ([Ignore] / [Ignore("...")]) or combined with
    // others ([Test, Ignore(...)]), i.e. preceded by an open bracket or a comma.
    private static readonly Regex IgnoreAttribute = new(
        @"(?:\[|,)\s*Ignore(\s*\(|\s*\])",
        RegexOptions.Compiled
    );

    private static readonly (string Name, Regex Pattern)[] LegacyAssertPatterns =
    [
        ("Assert.That", new Regex(@"\bAssert\.That\b", RegexOptions.Compiled)),
        ("Assert.AreEqual", new Regex(@"\bAssert\.AreEqual\b", RegexOptions.Compiled)),
        ("Assert.AreNotEqual", new Regex(@"\bAssert\.AreNotEqual\b", RegexOptions.Compiled)),
        ("Assert.AreSame", new Regex(@"\bAssert\.AreSame\b", RegexOptions.Compiled)),
        ("Assert.AreNotSame", new Regex(@"\bAssert\.AreNotSame\b", RegexOptions.Compiled)),
        ("Assert.IsTrue", new Regex(@"\bAssert\.IsTrue\b", RegexOptions.Compiled)),
        ("Assert.IsFalse", new Regex(@"\bAssert\.IsFalse\b", RegexOptions.Compiled)),
        ("Assert.IsNull", new Regex(@"\bAssert\.IsNull\b", RegexOptions.Compiled)),
        ("Assert.IsNotNull", new Regex(@"\bAssert\.IsNotNull\b", RegexOptions.Compiled)),
        ("Assert.IsEmpty", new Regex(@"\bAssert\.IsEmpty\b", RegexOptions.Compiled)),
        ("Assert.IsNotEmpty", new Regex(@"\bAssert\.IsNotEmpty\b", RegexOptions.Compiled)),
        ("Assert.Contains", new Regex(@"\bAssert\.Contains\b", RegexOptions.Compiled)),
    ];

    private static readonly Regex FixedDelay = new(
        @"\b(Task\.Delay|Thread\.Sleep)\s*\(",
        RegexOptions.Compiled
    );

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
            foreach (var (line, _) in SourceText.MatchingLines(stripped, IgnoreAttribute))
                offenders.Add($"{rel}:{line}");
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
                    offenders.Add($"{rel}:{line}  ({name})");
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

            var raw = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n');
            var stripped = SourceText
                .StripCommentsAndStrings(File.ReadAllText(file))
                .Replace("\r\n", "\n")
                .Split('\n');

            for (var i = 0; i < stripped.Length && i < raw.Length; i++)
            {
                if (!FixedDelay.IsMatch(stripped[i]))
                    continue;
                if (HasJustification(raw, i))
                    continue;
                offenders.Add($"{rel}:{i + 1}  -> {raw[i].Trim()}");
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
}
