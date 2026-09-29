namespace Trax.Core.Tests.Meta.Tests;

/// <summary>
/// A skip is a runtime decision with a reason in the output, not an attribute that hides.
///
/// <para>Enforces <c>Trax.Docs/adr/0005-a-skipped-test-is-a-runtime-decision.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0005-a-skipped-test-is-a-runtime-decision.md")]
[TestFixture]
public class NoIgnoreAttributeTests
{
    // The same two patterns as Trax.Core.Testing's HygieneGuards.NoIgnoreAttribute: an [Ignore]
    // anywhere in an attribute list (qualified, suffixed, or on a later line), and a per-case
    // Ignore = or IgnoreReason = on a case or fixture attribute. Matched over the whole file.
    private static readonly Regex[] IgnorePatterns =
    [
        new(
            @"(?:\[|,)\s*(?:[\w.]+\.)?Ignore(?:Attribute)?(?=\s*(?:\(|\]|,))",
            RegexOptions.Compiled
        ),
        new(
            @"\[[^\]]*?\b(?:TestCase|TestCaseSource|TestFixture|TestFixtureSource)(?:Attribute)?\b[^\]]*?\b(?:Ignore|IgnoreReason)\s*=(?!=)",
            RegexOptions.Compiled
        ),
    ];

    [Test]
    public void TestSources_DoNotUse_IgnoreAttribute()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles.CSharp("tests"))
        {
            // self-skip; this file mentions [Ignore] in a string literal
            if (file.EndsWith("NoIgnoreAttributeTests.cs", StringComparison.Ordinal))
                continue;

            foreach (var line in OffendingLines(File.ReadAllText(file)))
                offenders.Add($"{RepoRoot.Relative(file)}:{line}");
        }

        offenders
            .Should()
            .BeEmpty(
                "[Ignore] silently hides failing tests. Trax.Docs/reference/test-conventions.md > Skipping requires either "
                    + "fixing the underlying code, fixing the test premise, or using Assert.Ignore(\"reason\") "
                    + "at runtime with an explicit reachability check. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    // The patterns themselves, against each form a declaration-time skip takes, so a copy of this
    // guard that falls behind the shipped one fails here rather than passing a repo it cannot see.
    [TestCase("[Ignore(\"x\")] public void A() {}")]
    [TestCase("[Test, Ignore(\"x\")] public void A() {}")]
    [TestCase("[Test,\n Ignore(\"x\")] public void A() {}")]
    [TestCase("[NUnit.Framework.Ignore(\"x\")] public void A() {}")]
    [TestCase("[IgnoreAttribute(\"x\")] public void A() {}")]
    [TestCase("[TestCase(1, Ignore = \"x\")] public void A(int n) {}")]
    [TestCase("[TestCase(1, IgnoreReason = \"x\")] public void A(int n) {}")]
    public void Patterns_FlagEveryDeclarationTimeSkip(string member) =>
        OffendingLines("public class FooTests { " + member + " }")
            .Should()
            .NotBeEmpty("every one of these skips a test when it is declared");

    [TestCase("/* [Ignore] */ public void A() {}")]
    [TestCase("public void A() { Assert.Ignore(\"not reachable\"); }")]
    [TestCase("public void A() { var ignore = options.Ignore == true; }")]
    public void Patterns_LeaveRuntimeSkipsAndCommentsAlone(string member) =>
        OffendingLines("public class FooTests { " + member + " }").Should().BeEmpty();

    private static SortedSet<int> OffendingLines(string source)
    {
        var stripped = SourceText.StripCommentsAndStrings(source);
        var lines = new SortedSet<int>();
        foreach (var pattern in IgnorePatterns)
        foreach (Match match in pattern.Matches(stripped))
            lines.Add(stripped.Take(match.Index).Count(c => c == '\n') + 1);

        return lines;
    }
}
