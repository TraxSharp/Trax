using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>The exactly-once effect orchestration over the order machine (place = the irreversible action).</summary>
public class OrderSendTests
{
    private static SnapshotEffectRunner<OrderState, OrderTrigger> NewRunner(IEffect effect)
    {
        var ctx = TestDb.NewContext();
        var claims = new EfEffectClaimStore(ctx);
        var service = TestOrder.Service(new EfSnapshotStore(ctx), claims);
        return new SnapshotEffectRunner<OrderState, OrderTrigger>(
            service,
            effect,
            new IdempotentEffect(claims),
            OrderState.Review,
            OrderTrigger.Place,
            OrderState.Placed,
            TestOrder.EffectKey,
            receiptKey: "orderId"
        );
    }

    private static async Task SeedReview(string user, Guid id) =>
        (await TestOrder.Service(TestDb.NewStore()).Autosave(user, id, TestOrder.ReviewJson(1, 2)))
            .Should()
            .BeOfType<AutosaveResult.Saved>();

    private static async Task<Snapshot> Load(string user, Guid id) =>
        ((LoadResult.Loaded)await TestOrder.Service(TestDb.NewStore()).Load(user, id)).Snapshot;

    [Test]
    public async Task Places_an_order_exactly_once_and_folds_the_receipt_into_the_snapshot()
    {
        var id = Guid.NewGuid();
        await SeedReview("u", id);
        var effect = new CountingEffect();

        var outcome = await NewRunner(effect).Run("u", id, "req-1");

        outcome
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Placed");
        effect.Calls.Should().Be(1);
        (await Load("u", id)).Context["receipt"]!.GetValue<string>().Should().Be("receipt-1");
    }

    [Test]
    public async Task A_retry_after_the_order_is_placed_replays_and_never_delivers_again()
    {
        var id = Guid.NewGuid();
        await SeedReview("u", id);
        var effect = new CountingEffect();

        await NewRunner(effect).Run("u", id, "req-1");
        var retry = await NewRunner(effect).Run("u", id, "req-2");

        retry.Should().BeOfType<AdvanceOutcome.Advanced>();
        effect.Calls.Should().Be(1);
    }

    [Test]
    public async Task Twelve_concurrent_sends_deliver_exactly_once()
    {
        var id = Guid.NewGuid();
        await SeedReview("u", id);
        var effect = new CountingEffect();

        var outcomes = await Task.WhenAll(
            Enumerable
                .Range(0, 12)
                .Select(i => Task.Run(() => NewRunner(effect).Run("u", id, $"req-{i}")))
        );

        effect.Calls.Should().Be(1);
        outcomes.Should().Contain(o => o is AdvanceOutcome.Advanced);
        (await Load("u", id)).State.Should().Be("Placed");
    }

    [Test]
    public async Task A_second_send_while_the_first_is_mid_flight_is_refused_as_in_progress()
    {
        var id = Guid.NewGuid();
        await SeedReview("u", id);
        var gate = new GatedEffect();

        var running = Task.Run(() => NewRunner(gate).Run("u", id, "req-1"));
        await gate.Entered;

        var second = await NewRunner(gate).Run("u", id, "req-2");
        second
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be("effect-in-progress");

        gate.Release();
        (await running).Should().BeOfType<AdvanceOutcome.Advanced>();
        gate.Calls.Should().Be(1);
    }

    [Test]
    public async Task A_wrong_state_send_is_refused_before_the_effect_fires()
    {
        var id = Guid.NewGuid();
        await TestOrder.Service(TestDb.NewStore()).Autosave("u", id, TestOrder.DraftJson); // Draft, not Review
        var effect = new CountingEffect();

        var outcome = await NewRunner(effect).Run("u", id, "req");

        outcome
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be("no-transition");
        effect.Calls.Should().Be(0);
    }

    [Test]
    public async Task Two_users_send_independently()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedReview("user-a", a);
        await SeedReview("user-b", b);
        var effect = new CountingEffect();

        (await NewRunner(effect).Run("user-a", a, "r"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        (await NewRunner(effect).Run("user-b", b, "r"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();

        effect.Calls.Should().Be(2);
    }

    [Test]
    public async Task A_failed_delivery_leaves_the_draft_at_review_and_a_retry_delivers()
    {
        var id = Guid.NewGuid();
        await SeedReview("u", id);

        var failing = new CountingEffect(fail: true);
        var act = async () => await NewRunner(failing).Run("u", id, "r1");
        await act.Should().ThrowAsync<InvalidOperationException>();

        (await Load("u", id)).State.Should().Be("Review");

        var effect = new CountingEffect();
        (await NewRunner(effect).Run("u", id, "r2")).Should().BeOfType<AdvanceOutcome.Advanced>();
        effect.Calls.Should().Be(1);
    }

    [Test]
    public async Task A_reset_releases_the_claim_so_the_next_order_delivers_afresh()
    {
        var id = Guid.NewGuid();
        await SeedReview("u", id);
        var effect = new CountingEffect();

        await NewRunner(effect).Run("u", id, "r1"); // Placed, receipt-1

        // Reset back to the initial state releases the effect claim.
        var ctx = TestDb.NewContext();
        var reset = await TestOrder
            .Service(new EfSnapshotStore(ctx), new EfEffectClaimStore(ctx))
            .Advance("u", id, "Reset", requestId: "reset");
        reset
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Draft");

        await SeedReview("u", id);
        (await NewRunner(effect).Run("u", id, "r2")).Should().BeOfType<AdvanceOutcome.Advanced>();
        effect.Calls.Should().Be(2);
    }

    // Gap (test-catalog §8.2): the effect succeeds but the state write never lands (a crash between
    // recording the receipt and committing the terminal state). A retry must REPLAY the receipt and
    // commit, delivering exactly once — asserted here by fault injection, not just by construction.
    [Test]
    public async Task An_effect_that_ran_but_never_committed_is_replayed_by_the_retry_without_re_delivering()
    {
        var id = Guid.NewGuid();
        await SeedReview("u", id);
        var effect = new CountingEffect();

        // The effect runs and records its receipt, but we "crash" before advancing to Placed.
        var ctx = TestDb.NewContext();
        var ran = await new IdempotentEffect(new EfEffectClaimStore(ctx)).RunOnce(
            TestOrder.EffectKey("u", id),
            () => effect.Run(TestOrder.Machine.Definition.CreateInitialSnapshot())
        );
        ran.Should().BeOfType<EffectOutcome.Ran>();
        (await Load("u", id)).State.Should().Be("Review"); // never advanced

        // The retry finds the recorded receipt (AlreadyRan), does NOT re-deliver, and commits Placed.
        var outcome = await NewRunner(effect).Run("u", id, "retry");
        outcome
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Placed");
        effect.Calls.Should().Be(1);
        (await Load("u", id)).Context["receipt"]!.GetValue<string>().Should().Be("receipt-1");
    }
}
