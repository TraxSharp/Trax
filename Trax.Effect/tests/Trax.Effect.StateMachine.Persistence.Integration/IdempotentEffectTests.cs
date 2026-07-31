using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

public class IdempotentEffectTests
{
    private static Snapshot Snap() =>
        new()
        {
            Machine = "x",
            Version = 1,
            State = "s",
            Context = new JsonObject(),
        };

    private static string Key() => $"idem:{Guid.NewGuid()}";

    [Test]
    public async Task Runs_the_effect_once_then_replays_the_recorded_receipt()
    {
        var key = Key();
        var effect = new CountingEffect();

        var first = await new IdempotentEffect(TestDb.NewClaims()).RunOnce(
            key,
            () => effect.Run(Snap())
        );
        var second = await new IdempotentEffect(TestDb.NewClaims()).RunOnce(
            key,
            () => effect.Run(Snap())
        );

        first.Should().BeOfType<EffectOutcome.Ran>().Which.Receipt.Should().Be("receipt-1");
        second.Should().BeOfType<EffectOutcome.AlreadyRan>().Which.Receipt.Should().Be("receipt-1");
        effect.Calls.Should().Be(1);
    }

    [Test]
    public async Task A_second_caller_while_the_first_is_mid_flight_reports_in_progress_and_does_not_run()
    {
        var key = Key();
        var gate = new GatedEffect();

        var running = Task.Run(() =>
            new IdempotentEffect(TestDb.NewClaims()).RunOnce(key, () => gate.Run(Snap()))
        );
        await gate.Entered; // the first caller now holds the claim, blocked inside the effect

        var second = await new IdempotentEffect(TestDb.NewClaims()).RunOnce(
            key,
            () => Task.FromResult("should-not-run")
        );
        second.Should().BeOfType<EffectOutcome.InProgress>();

        gate.Release();
        (await running).Should().BeOfType<EffectOutcome.Ran>();
        gate.Calls.Should().Be(1);
    }

    [Test]
    public async Task Sixteen_concurrent_runs_fire_the_effect_exactly_once()
    {
        var key = Key();
        var effect = new CountingEffect();

        var outcomes = await Task.WhenAll(
            Enumerable
                .Range(0, 16)
                .Select(_ =>
                    Task.Run(() =>
                        new IdempotentEffect(TestDb.NewClaims()).RunOnce(
                            key,
                            () => effect.Run(Snap())
                        )
                    )
                )
        );

        effect.Calls.Should().Be(1);
        outcomes.Count(o => o is EffectOutcome.Ran).Should().Be(1);
        outcomes
            .Should()
            .OnlyContain(o =>
                o is EffectOutcome.Ran
                || o is EffectOutcome.AlreadyRan
                || o is EffectOutcome.InProgress
            );
    }

    [Test]
    public async Task A_failed_effect_releases_the_claim_so_a_retry_re_runs()
    {
        var key = Key();
        var failing = new CountingEffect(fail: true);

        var act = async () =>
            await new IdempotentEffect(TestDb.NewClaims()).RunOnce(key, () => failing.Run(Snap()));
        await act.Should().ThrowAsync<InvalidOperationException>();

        // The claim was released, so a retry with a working effect runs.
        var effect = new CountingEffect();
        var retry = await new IdempotentEffect(TestDb.NewClaims()).RunOnce(
            key,
            () => effect.Run(Snap())
        );
        retry.Should().BeOfType<EffectOutcome.Ran>();
        effect.Calls.Should().Be(1);
    }

    [Test]
    public async Task A_released_key_re_runs_and_distinct_keys_are_independent()
    {
        var key = Key();
        var effect = new CountingEffect();

        await new IdempotentEffect(TestDb.NewClaims()).RunOnce(key, () => effect.Run(Snap()));
        await TestDb.NewClaims().Release(key);
        var reRun = await new IdempotentEffect(TestDb.NewClaims()).RunOnce(
            key,
            () => effect.Run(Snap())
        );

        reRun.Should().BeOfType<EffectOutcome.Ran>();
        effect.Calls.Should().Be(2);

        // A different key is untouched by all of the above.
        var other = await new IdempotentEffect(TestDb.NewClaims()).RunOnce(
            Key(),
            () => effect.Run(Snap())
        );
        other.Should().BeOfType<EffectOutcome.Ran>();
    }

    [Test]
    public async Task Receipts_round_trip_intact_including_quotes_accents_and_emoji()
    {
        var key = Key();
        const string tricky = "recu\"\n\té\U0001F680-123";

        var outcome = await new IdempotentEffect(TestDb.NewClaims()).RunOnce(
            key,
            () => Task.FromResult(tricky)
        );

        outcome.Should().BeOfType<EffectOutcome.Ran>().Which.Receipt.Should().Be(tricky);
        (await TestDb.NewClaims().GetReceipt(key)).Should().Be(tricky);
    }
}
