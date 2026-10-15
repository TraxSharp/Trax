using AwesomeAssertions;
using Trax.Api.GraphQL.TypeModules;

namespace Trax.Api.Tests;

/// <summary>
/// A caller holds at most <see cref="RunProgressLimits.PerRun"/> runProgress streams on one run and
/// <see cref="RunProgressLimits.PerPrincipal"/> in all, and a stream's place comes back once when it ends.
/// </summary>
[TestFixture]
[Property("adr", Adr)]
public class RunProgressLimitsTests
{
    private const string Adr =
        "Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md";

    [Test]
    public void One_run_holds_at_most_its_cap_and_another_caller_is_not_counted()
    {
        var limits = new RunProgressLimits();
        var held = Enumerable
            .Range(0, RunProgressLimits.PerRun)
            .Select(_ => limits.TryAcquire("alice", "run"))
            .ToList();

        held.Should().NotContainNulls();
        limits
            .TryAcquire("alice", "run")
            .Should()
            .BeNull("a caller holds at most PerRun streams on one run, per " + Adr);
        limits.TryAcquire("bob", "run").Should().NotBeNull();
        limits.TryAcquire("alice", "other").Should().NotBeNull();
    }

    [Test]
    public void A_caller_holds_at_most_its_cap_across_runs()
    {
        var limits = new RunProgressLimits();
        for (var i = 0; i < RunProgressLimits.PerPrincipal; i++)
            limits.TryAcquire("alice", $"run-{i}").Should().NotBeNull();

        limits
            .TryAcquire("alice", "one-more")
            .Should()
            .BeNull("a caller holds at most PerPrincipal streams, per " + Adr);
    }

    [Test]
    public void A_released_place_comes_back_once_however_often_it_is_released()
    {
        var limits = new RunProgressLimits();
        var held = Enumerable
            .Range(0, RunProgressLimits.PerRun)
            .Select(_ => limits.TryAcquire("alice", "run")!)
            .ToList();

        held[0].Dispose();
        held[0].Dispose();

        limits.TryAcquire("alice", "run").Should().NotBeNull();
        limits.TryAcquire("alice", "run").Should().BeNull();
    }
}
