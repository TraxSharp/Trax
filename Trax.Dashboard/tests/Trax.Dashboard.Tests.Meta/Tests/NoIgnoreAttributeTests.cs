using Trax.Core.Testing;
using Trax.Core.Testing.Guards;

namespace Trax.Dashboard.Tests.Meta.Tests;

/// <summary>
/// A skip is a runtime decision with a reason in the output, not an attribute that hides.
/// The check is the shipped <see cref="HygieneGuards.NoIgnoreAttribute"/>, which also sees an
/// <c>Ignore</c> combined with other attributes, a qualified or suffixed name, and a per-case
/// <c>Ignore =</c> or <c>IgnoreReason =</c>.
///
/// <para>Enforces <c>Trax.Docs/adr/0005-a-skipped-test-is-a-runtime-decision.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0005-a-skipped-test-is-a-runtime-decision.md")]
[TestFixture]
public class NoIgnoreAttributeTests
{
    [Test]
    public void TestSources_DoNotUse_IgnoreAttribute() => AssertClean(RepoRoot.Path);

    [TestCase("[Test, Ignore(\"later\")] public void T() { }")]
    [TestCase("[TestCase(1, Ignore = \"later\")] public void T(int i) { }")]
    [TestCase("[NUnit.Framework.IgnoreAttribute(\"later\")] public void T() { }")]
    public void Guard_fails_on_a_skip_attribute(string member)
    {
        using var repo = new SyntheticRepo().Write(
            "tests/Sample/SampleTests.cs",
            $"public class SampleTests {{ {member} }}"
        );

        var act = () => AssertClean(repo.Root);

        act.Should().Throw<AssertionException>().WithMessage("*SampleTests.cs:1*");
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
        var result = HygieneGuards.NoIgnoreAttribute(
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
                "Trax.Docs/adr/0005-a-skipped-test-is-a-runtime-decision.md: a skip is "
                    + "Assert.Ignore(\"reason\") at runtime, never an attribute. "
                    + result.FailureMessage
            );
    }
}
