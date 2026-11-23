using System.Globalization;

namespace Trax.Core.Tests.Meta.Tests;

/// <summary>
/// AwesomeAssertions only, because the because argument is where a failure explains itself.
///
/// <para>Enforces <c>Trax.Docs/adr/0004-tests-assert-with-fluentassertions.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0004-tests-assert-with-fluentassertions.md")]
[TestFixture]
public partial class NoLegacyAssertTests
{
    private static readonly (string Name, Regex Pattern)[] LegacyPatterns = new[]
    {
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
    };

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

    [Test]
    public void TestSources_UseOnly_AwesomeAssertions()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles.CSharp("tests"))
        {
            if (file.EndsWith("NoLegacyAssertTests.cs", StringComparison.Ordinal))
                continue;

            var content = File.ReadAllText(file);
            var stripped = SourceText.StripCommentsAndStrings(content);

            foreach (var (name, pattern) in LegacyPatterns)
            {
                var hits = SourceText.MatchingLines(stripped, pattern);
                foreach (var (line, _) in hits)
                    offenders.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{RepoRoot.Relative(file)}:{line}  ({name})"
                        )
                    );
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "Trax.Docs/contributing/test-conventions.md > Assertions requires AwesomeAssertions exclusively. "
                    + "Replace classic NUnit asserts (Assert.That, Assert.AreEqual, Assert.IsTrue, ...) with "
                    + ".Should().Be(...), .Should().BeTrue(), etc. "
                    + "Assert.Pass / Assert.Fail / Assert.Ignore remain acceptable. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    // The patterns themselves, so a copy of this guard that falls behind fails here.
    [TestCase("Assert.AreEqual(1, x);")]
    [TestCase("Assert.That(x, Is.EqualTo(1));")]
    [TestCase("ClassicAssert.AreEqual(1, x);")]
    [TestCase("CollectionAssert.IsEmpty(items);")]
    [TestCase("StringAssert.Contains(\"a\", text);")]
    public void Patterns_FlagEveryClassicAssert(string statement) =>
        LegacyPatterns
            .Where(p => p.Pattern.IsMatch(statement))
            .Should()
            .NotBeEmpty("a classic NUnit assertion is not AwesomeAssertions");

    [TestCase("x.Should().Be(1);")]
    [TestCase("Assert.Ignore(\"not reachable\");")]
    [TestCase("Assert.Fail(\"unreachable\");")]
    public void Patterns_LeaveAwesomeAssertionsAlone(string statement) =>
        LegacyPatterns.Where(p => p.Pattern.IsMatch(statement)).Should().BeEmpty();
}
