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

    [TestCase("[TestCase(1, Ignore = \"x\")]")]
    [TestCase("[TestCase(1, IgnoreReason = \"x\")]")]
    [TestCase("[NUnit.Framework.Ignore(\"x\")]")]
    [TestCase("[IgnoreAttribute(\"x\")]")]
    [TestCase("[Test,\n Ignore(\"x\")]")]
    public void NoIgnoreAttribute_FlagsEveryDeclarationTimeSkip(string attribute)
    {
        using var repo = new TempRepo().Write(
            "tests/Sample/FooTests.cs",
            "public class FooTests { " + attribute + " public void A(int n) {} }"
        );

        HygieneGuards
            .NoIgnoreAttribute(OptionsFor(repo))
            .Passed.Should()
            .BeFalse($"{attribute} skips the test at declaration time, which ADR 0005 rejects");
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

    #region NoUnsynchronisedAssertAfterStart

    private static string StartedThen(string between) =>
        "public class FooTests {\n"
        + "    public async Task A() {\n"
        + "        await service.StartAsync(CancellationToken.None);\n"
        + between
        + "        await train.Received(1).Run(Arg.Any<int>());\n"
        + "    }\n"
        + "}";

    [Test]
    public void NoUnsynchronisedAssertAfterStart_FlagsACheckStraightAfterTheStart()
    {
        using var repo = new TempRepo().Write("tests/Sample/FooTests.cs", StartedThen(""));

        var result = HygieneGuards.NoUnsynchronisedAssertAfterStart(OptionsFor(repo));

        result.Passed.Should().BeFalse();
        result.Offenders.Should().ContainSingle(o => o.Contains("started at line 3"));
    }

    [Test]
    public void NoUnsynchronisedAssertAfterStart_FlagsAStartThroughATestHelper()
    {
        using var repo = new TempRepo().Write(
            "tests/Sample/FooTests.cs",
            "public class FooTests {\n"
                + "    public async Task A() {\n"
                + "        var first = await StartAsync();\n"
                + "        await scheduler.DidNotReceive().Requeue();\n"
                + "    }\n"
                + "    private async Task<int> StartAsync() => await jobs.StartAsync();\n"
                + "}"
        );

        HygieneGuards
            .NoUnsynchronisedAssertAfterStart(OptionsFor(repo))
            .Offenders.Should()
            .ContainSingle(o => o.Contains("FooTests.cs:4"));
    }

    [TestCase("        await trainHit.Task.WaitAsync(TimeSpan.FromSeconds(10));\n")]
    [TestCase("        (await WaitUntilAsync(() => ran, Timeout)).Should().BeTrue();\n")]
    [TestCase("        await service.StopAsync(CancellationToken.None);\n")]
    [TestCase("        await cycles.WhenReached(2);\n")]
    [TestCase("        await jobs.Current;\n")]
    [TestCase("        var handler = await started;\n")]
    public void NoUnsynchronisedAssertAfterStart_AllowsACheckAfterAWait(string wait)
    {
        using var repo = new TempRepo().Write("tests/Sample/FooTests.cs", StartedThen(wait));

        HygieneGuards.NoUnsynchronisedAssertAfterStart(OptionsFor(repo)).Passed.Should().BeTrue();
    }

    [Test]
    public void NoUnsynchronisedAssertAfterStart_AllowsAStartMarkedSynchronous()
    {
        using var repo = new TempRepo().Write(
            "tests/Sample/FooTests.cs",
            StartedThen("")
                .Replace(
                    "        await service.StartAsync",
                    "        // synchronous-start: the gate checks inside the awaited StartAsync.\n"
                        + "        await service.StartAsync"
                )
        );

        HygieneGuards.NoUnsynchronisedAssertAfterStart(OptionsFor(repo)).Passed.Should().BeTrue();
    }

    [Test]
    public void NoUnsynchronisedAssertAfterStart_DoesNotCarryAStartIntoTheNextMethod()
    {
        using var repo = new TempRepo().Write(
            "tests/Sample/FooTests.cs",
            "public class FooTests {\n"
                + "    public async Task A() { await service.StartAsync(default); }\n"
                + "    public async Task B() {\n"
                + "        await train.Received(1).Run(Arg.Any<int>());\n"
                + "    }\n"
                + "}"
        );

        HygieneGuards.NoUnsynchronisedAssertAfterStart(OptionsFor(repo)).Passed.Should().BeTrue();
    }

    #endregion
}
