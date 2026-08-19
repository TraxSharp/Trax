using FluentAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The runtime self-check: a machine replays its committed differential corpus through its own engine, and a
/// host aggregates the results at startup (<see cref="SnapshotSelfCheck.Run"/>). Proves a machine with a
/// matching corpus reports no diffs, a tampered one reports them, a machine with no corpus is skipped (not a
/// failure), and the aggregation is machine-prefixed. The fakes are parameterless with unique ids so the
/// assembly-scan discovery (which activates every IMachine) stays happy.
/// </summary>
public class SnapshotSelfCheckTests
{
    // A corpus the declarative turnstile reproduces exactly: a transition (Locked + Coin(quarter) -> Unlocked)
    // and a rejection (Locked + Push has no transition), so replay exercises both engine outcomes.
    private const string PassCorpus = """
        {"cases":[{"given":{"machine":"self-check-pass","version":1,"state":"Locked","context":{}},"when":{"trigger":"Coin","input":{"coin":"quarter"}},"expect":{"outcome":"transitioned","wire":"{\"machine\":\"self-check-pass\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"paidWith\":\"quarter\"}}"}},{"given":{"machine":"self-check-pass","version":1,"state":"Locked","context":{}},"when":{"trigger":"Push"},"expect":{"outcome":"rejected","reason":"no-transition"}}]}
        """;

    // Same case, but the golden expects the wrong wire — the engine's real result diverges from it.
    private const string FailCorpus = """
        {"cases":[{"given":{"machine":"self-check-fail","version":1,"state":"Locked","context":{}},"when":{"trigger":"Coin","input":{"coin":"quarter"}},"expect":{"outcome":"transitioned","wire":"WRONG"}}]}
        """;

    private sealed class SelfCheckPassMachine : Machine<TurnstileState, TurnstileTrigger>
    {
        protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
            DeclarativeTurnstileMachine.ConfigureTurnstile(m, "self-check-pass");

        public override string? Corpus => PassCorpus;
    }

    private sealed class SelfCheckFailMachine : Machine<TurnstileState, TurnstileTrigger>
    {
        protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
            DeclarativeTurnstileMachine.ConfigureTurnstile(m, "self-check-fail");

        public override string? Corpus => FailCorpus;
    }

    [Test]
    public void SelfCheck_returns_no_diffs_when_the_engine_reproduces_the_corpus()
    {
        new SelfCheckPassMachine().SelfCheck().Should().BeEmpty();
    }

    [Test]
    public void SelfCheck_reports_a_diff_when_the_engine_diverges_from_the_corpus()
    {
        new SelfCheckFailMachine()
            .SelfCheck()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("Locked + Coin");
    }

    [Test]
    public void SelfCheck_is_empty_for_a_machine_that_ships_no_corpus()
    {
        new DeclarativeTurnstileMachine().SelfCheck().Should().BeEmpty();
    }

    [Test]
    public void Run_aggregates_diffs_across_machines_prefixed_by_name_and_skips_corpus_less_ones()
    {
        var machines = new IMachine[]
        {
            new DeclarativeTurnstileMachine(), // no corpus -> skipped
            new SelfCheckPassMachine(), // agrees -> no diff
            new SelfCheckFailMachine(), // diverges -> one diff
        };

        SnapshotSelfCheck
            .Run(machines)
            .Should()
            .ContainSingle()
            .Which.Should()
            .StartWith("self-check-fail: ");
    }

    [Test]
    public void Run_is_empty_when_no_machine_ships_a_corpus()
    {
        SnapshotSelfCheck
            .Run(new IMachine[] { new DeclarativeTurnstileMachine() })
            .Should()
            .BeEmpty();
    }
}
