using Trax.Core.Testing;
using Trax.Core.Testing.Guards;

namespace Trax.Core.Testing.Tests;

[TestFixture]
public class HygieneGuardsTests
{
    private static ArchitectureGuardOptions OptionsFor(TempRepo repo) =>
        new() { RepoRootOverride = repo.Root, TestScanRoots = ["tests"] };

    #region NoIgnoreAttribute

    [Test]
    public void NoIgnoreAttribute_FlagsIgnoredTest()
    {
        using var repo = new TempRepo().Write(
            "tests/Sample/FooTests.cs",
            "public class FooTests { [Test, Ignore(\"x\")] public void A() {} }"
        );

        var result = HygieneGuards.NoIgnoreAttribute(OptionsFor(repo));

        result.Passed.Should().BeFalse();
        result.Offenders.Should().ContainSingle(o => o.Contains("tests/Sample/FooTests.cs"));
        result.Inspected.Should().Be(1);
    }

    [Test]
    public void NoIgnoreAttribute_IgnoresCommentedOccurrence()
    {
        using var repo = new TempRepo().Write(
            "tests/Sample/FooTests.cs",
            "public class FooTests { /* [Ignore] in a comment */ public void A() {} }"
        );

        HygieneGuards.NoIgnoreAttribute(OptionsFor(repo)).Passed.Should().BeTrue();
    }

    [Test]
    public void NoIgnoreAttribute_RespectsAllowlist()
    {
        using var repo = new TempRepo().Write(
            "tests/Sample/FooTests.cs",
            "public class FooTests { [Ignore(\"gated\")] public void A() {} }"
        );

        var options = OptionsFor(repo) with
        {
            NoIgnoreKnownExceptions = new HashSet<string> { "tests/Sample/FooTests.cs" },
        };

        HygieneGuards.NoIgnoreAttribute(options).Passed.Should().BeTrue();
    }

    #endregion

    #region NoLegacyAsserts

    [Test]
    public void NoLegacyAsserts_FlagsClassicAssert()
    {
        using var repo = new TempRepo().Write(
            "tests/Sample/FooTests.cs",
            "public class FooTests { public void A() { Assert.AreEqual(1, 1); } }"
        );

        var result = HygieneGuards.NoLegacyAsserts(OptionsFor(repo));

        result.Passed.Should().BeFalse();
        result.Offenders.Should().ContainSingle(o => o.Contains("Assert.AreEqual"));
    }

    [Test]
    public void NoLegacyAsserts_AllowsFluentStyle()
    {
        using var repo = new TempRepo().Write(
            "tests/Sample/FooTests.cs",
            "public class FooTests { public void A() { result.Should().Be(1); } }"
        );

        HygieneGuards.NoLegacyAsserts(OptionsFor(repo)).Passed.Should().BeTrue();
    }

    #endregion

    #region NoFixedDelays

    [Test]
    public void NoFixedDelays_FlagsUnjustifiedDelay()
    {
        using var repo = new TempRepo().Write(
            "tests/Sample/FooTests.cs",
            "public class FooTests { public async Task A() { await Task.Delay(2000); } }"
        );

        var result = HygieneGuards.NoFixedDelays(OptionsFor(repo));

        result.Passed.Should().BeFalse();
        result.Offenders.Should().ContainSingle(o => o.Contains("Task.Delay"));
    }

    [Test]
    public void NoFixedDelays_AllowsJustifiedDelay()
    {
        using var repo = new TempRepo().Write(
            "tests/Sample/FooTests.cs",
            "public class FooTests { public async Task A() {\n"
                + "    // measuring-interval: prove two stamps are 50ms apart\n"
                + "    await Task.Delay(50);\n"
                + "} }"
        );

        HygieneGuards.NoFixedDelays(OptionsFor(repo)).Passed.Should().BeTrue();
    }

    #endregion
}
