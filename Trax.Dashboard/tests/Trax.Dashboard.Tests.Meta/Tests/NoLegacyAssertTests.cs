namespace Trax.Dashboard.Tests.Meta.Tests;

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
                "CLAUDE.md > Naming Conventions requires FluentAssertions exclusively. "
                    + "Replace classic NUnit asserts with .Should().Be(...), .Should().BeTrue(), etc. "
                    + "Assert.Pass / Assert.Fail / Assert.Ignore remain acceptable. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }
}
