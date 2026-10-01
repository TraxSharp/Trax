using Trax.Core.Testing;
using Trax.Core.Testing.Guards;

namespace Trax.Dashboard.Tests.Meta.Tests;

/// <summary>
/// Tests wait on the condition that means the work finished, not on a duration.
/// The check is the shipped <see cref="HygieneGuards.NoFixedDelays"/>: a <c>Task.Delay</c> or
/// <c>Thread.Sleep</c> in a test passes only with a <c>determinism:</c>, <c>allowed-delay:</c>,
/// <c>measuring-interval:</c> or <c>negative-wait:</c> comment on its line or up to three above.
/// This repo has no file exempt from it.
///
/// <para>Enforces <c>Trax.Docs/adr/0006-tests-synchronise-on-a-signal.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0006-tests-synchronise-on-a-signal.md")]
[TestFixture]
public class NoFixedTaskDelayTests
{
    [Test]
    public void TestSources_DoNotIntroduce_NewFixedDelays() => AssertClean(RepoRoot.Path);

    [Test]
    public void Guard_fails_on_an_unjustified_delay()
    {
        using var repo = new SyntheticRepo().Write(
            "tests/Sample/SampleTests.cs",
            "public class SampleTests { public async Task T() { await Task.Delay(100); } }"
        );

        var act = () => AssertClean(repo.Root);

        act.Should().Throw<AssertionException>().WithMessage("*SampleTests.cs:1*");
    }

    [Test]
    public void Guard_accepts_a_justified_delay()
    {
        using var repo = new SyntheticRepo().Write(
            "tests/Sample/SampleTests.cs",
            "public class SampleTests {\n"
                + "    // negative-wait: nothing may arrive in this window\n"
                + "    public async Task T() { await Task.Delay(100); }\n"
                + "}"
        );

        var act = () => AssertClean(repo.Root);

        act.Should().NotThrow();
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
        var result = HygieneGuards.NoFixedDelays(
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
                "Trax.Docs/adr/0006-tests-synchronise-on-a-signal.md: synchronise on the "
                    + "completion signal, not a fixed duration. "
                    + result.FailureMessage
            );
    }
}
