using System.Diagnostics;
using FluentAssertions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Tests.Stress.Fakes;
using Trax.Effect.StateMachine.Tests.Stress.Fixtures;

namespace Trax.Effect.StateMachine.Tests.Stress.IntegrationTests;

/// <summary>
/// The exactly-once send under real contention at scale: many drafts each raced by several sends, and one
/// hot draft raced by a crowd. The proof is the total delivery count from a shared counting effect.
/// </summary>
[TestFixture]
public class ExactlyOnceStressTests : StressFixture
{
    private static async Task SeedReview(Guid id)
    {
        await using var ctx = StressDb.NewContext();
        (
            await StressOrder
                .Service(new EfSnapshotStore(ctx))
                .Autosave("u", id, StressOrder.ReviewJson(1, 2))
        )
            .Should()
            .BeOfType<AutosaveResult.Saved>();
    }

    private static async Task<string> LoadState(Guid id)
    {
        await using var ctx = StressDb.NewContext();
        var loaded = await StressOrder.Service(new EfSnapshotStore(ctx)).Load("u", id);
        return loaded is LoadResult.Loaded l ? l.Snapshot.State : "not-loaded";
    }

    private static async Task<AdvanceOutcome> Send(Guid id, string requestId, IEffect effect)
    {
        await using var ctx = StressDb.NewContext();
        var claims = new EfEffectClaimStore(ctx);
        var runner = new SnapshotEffectRunner<OrderState, OrderTrigger>(
            StressOrder.Service(new EfSnapshotStore(ctx), claims),
            effect,
            new IdempotentEffect(claims),
            OrderState.Review,
            OrderTrigger.Place,
            OrderState.Placed,
            StressOrder.EffectKey,
            receiptKey: "orderId"
        );
        return await runner.Run("u", id, requestId);
    }

    [Test]
    public async Task Many_drafts_each_raced_by_several_sends_deliver_exactly_once_each()
    {
        var instances = StressProfile.Instances;
        var per = StressProfile.SendsPerInstance;
        var effect = new CountingCharge();
        var ids = Enumerable.Range(0, instances).Select(_ => Guid.NewGuid()).ToArray();

        await Fan(
            instances,
            StressProfile.MaxConcurrency,
            async i =>
            {
                await SeedReview(ids[i]);
                return 0;
            }
        );

        var sw = Stopwatch.StartNew();
        await Fan(
            instances * per,
            StressProfile.MaxConcurrency,
            k => Send(ids[k / per], $"req-{k}", effect)
        );
        sw.Stop();

        // The whole point: N drafts charged once each means exactly N deliveries, however many sends raced.
        effect
            .Calls.Should()
            .Be(instances, "each order charges exactly once under {0} racing sends", per);

        var states = await Fan(instances, StressProfile.MaxConcurrency, i => LoadState(ids[i]));
        states.Should().OnlyContain(s => s == "Placed", "every draft ends Placed with a receipt");

        var sends = instances * per;
        TestContext.Progress.WriteLine(
            $"[exactly-once] {instances} drafts x {per} sends = {sends} sends in {sw.ElapsedMilliseconds} ms "
                + $"({sends * 1000.0 / Math.Max(1, sw.ElapsedMilliseconds):N0} sends/s), deliveries={effect.Calls}"
        );
    }

    [Test]
    public async Task A_single_hot_draft_under_a_crowd_of_sends_delivers_once()
    {
        var id = Guid.NewGuid();
        await SeedReview(id);
        var effect = new CountingCharge();
        var hot = StressProfile.HotSends;

        var outcomes = await Fan(
            hot,
            StressProfile.MaxConcurrency,
            k => Send(id, $"req-{k}", effect)
        );

        effect
            .Calls.Should()
            .Be(1, "one hot draft charges exactly once under {0} concurrent sends", hot);
        outcomes.Should().Contain(o => o is AdvanceOutcome.Advanced);
        (await LoadState(id)).Should().Be("Placed");
    }
}
