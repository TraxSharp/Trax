namespace Trax.Core.Tests.Meta.Tests;

/// <summary>
/// FluentAssertions only, because the because argument is where a failure explains itself.
///
/// <para>Enforces <c>Trax.Docs/adr/0004-tests-assert-with-fluentassertions.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0004-tests-assert-with-fluentassertions.md")]
[TestFixture]
public class NoLegacyAssertTests
{
    private static readonly (string Name, Regex Pattern)[] LegacyPatterns = new[]
    {
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
        ("ClassicAssert", new Regex(@"\bClassicAssert\.\w+", RegexOptions.Compiled)),
        ("CollectionAssert", new Regex(@"\bCollectionAssert\.\w+", RegexOptions.Compiled)),
        ("StringAssert", new Regex(@"\bStringAssert\.\w+", RegexOptions.Compiled)),
    };

    [Test]
    public void TestSources_UseOnly_FluentAssertions()
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
                    offenders.Add($"{RepoRoot.Relative(file)}:{line}  ({name})");
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "Trax.Docs/reference/test-conventions.md > Assertions requires FluentAssertions exclusively. "
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
            .NotBeEmpty("a classic NUnit assertion is not FluentAssertions");

    [TestCase("x.Should().Be(1);")]
    [TestCase("Assert.Ignore(\"not reachable\");")]
    [TestCase("Assert.Fail(\"unreachable\");")]
    public void Patterns_LeaveFluentAssertionsAlone(string statement) =>
        LegacyPatterns.Where(p => p.Pattern.IsMatch(statement)).Should().BeEmpty();
}
