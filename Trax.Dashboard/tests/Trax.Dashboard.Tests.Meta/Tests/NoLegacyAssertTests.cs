using Trax.Core.Testing;
using Trax.Core.Testing.Guards;

namespace Trax.Dashboard.Tests.Meta.Tests;

/// <summary>
/// FluentAssertions only, because the because argument is where a failure explains itself.
/// The check is the shipped <see cref="HygieneGuards.NoLegacyAsserts"/>, so this repo applies the
/// same patterns as every other one, <c>ClassicAssert</c>, <c>CollectionAssert</c> and
/// <c>StringAssert</c> included.
///
/// <para>Enforces <c>Trax.Docs/adr/0004-tests-assert-with-fluentassertions.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0004-tests-assert-with-fluentassertions.md")]
[TestFixture]
public class NoLegacyAssertTests
{
    [Test]
    public void TestSources_UseOnly_FluentAssertions() => AssertClean(RepoRoot.Path);

    [Test]
    public void Guard_fails_on_a_classic_assert()
    {
        using var repo = new SyntheticRepo().Write(
            "tests/Sample/SampleTests.cs",
            "public class SampleTests { public void T() { ClassicAssert.AreEqual(1, 1); } }"
        );

        var act = () => AssertClean(repo.Root);

        act.Should().Throw<AssertionException>().WithMessage("*ClassicAssert*");
    }

    [Test]
    public void Guard_fails_when_it_finds_no_test_source()
    {
        using var repo = new SyntheticRepo();

        var act = () => AssertClean(repo.Root);

        act.Should().Throw<AssertionException>().WithMessage("*inspected no*");
    }

    private static void AssertClean(string root)
    {
        var result = HygieneGuards.NoLegacyAsserts(
            new ArchitectureGuardOptions { RepoRootOverride = root }
        );

        result
            .Inspected.Should()
            .BeGreaterThan(
                0,
                "the guard inspected no test sources under tests/, so it checked nothing"
            );
        result
            .Offenders.Should()
            .BeEmpty(
                "Trax.Docs/adr/0004-tests-assert-with-fluentassertions.md requires FluentAssertions "
                    + "exclusively. "
                    + result.FailureMessage
            );
    }
}
