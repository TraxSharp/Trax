using System.Diagnostics;
using AwesomeAssertions;
using Trax.StateMachine.Persistence;
using Trax.StateMachine.Tests.Stress.Fakes;
using Trax.StateMachine.Tests.Stress.Fixtures;

namespace Trax.StateMachine.Tests.Stress.IntegrationTests;

/// <summary>
/// High-volume autosave + advance throughput. This measures the two hot paths (soft autosave and
/// authoritative advance) across many instances and reports ops/sec. Correctness is the assertion; the
/// rate is logged, not asserted, since an absolute throughput floor would be machine-dependent and flaky.
/// </summary>
[TestFixture]
public class ThroughputStressTests : StressFixture
{
    private static async Task<AutosaveResult> Autosave(Guid id, string snapshot)
    {
        var ctx = StressDb.NewContext();
        await using var ctxDisposal = ctx.ConfigureAwait(false);
        return await StressOrder
            .Service(StressDb.NewStore(ctx))
            .Autosave("u", id, snapshot)
            .ConfigureAwait(false);
    }

    private static async Task<AdvanceOutcome> Advance(Guid id, string trigger)
    {
        var ctx = StressDb.NewContext();
        await using var ctxDisposal = ctx.ConfigureAwait(false);
        return await StressOrder
            .Service(StressDb.NewStore(ctx))
            .Advance("u", id, trigger)
            .ConfigureAwait(false);
    }

    private static async Task<string> LoadState(Guid id)
    {
        var ctx = StressDb.NewContext();
        await using var ctxDisposal = ctx.ConfigureAwait(false);
        var loaded = await StressOrder
            .Service(StressDb.NewStore(ctx))
            .Load("u", id)
            .ConfigureAwait(false);
        return loaded is LoadResult.Loaded l ? l.Snapshot.State : "not-loaded";
    }

    [Test]
    public async Task Autosave_and_advance_at_volume_stay_correct()
    {
        var n = StressProfile.ThroughputInstances;
        var ids = Enumerable.Range(0, n).Select(_ => Guid.NewGuid()).ToArray();
        var ops = 0;

        var sw = Stopwatch.StartNew();
        await Fan(
            n,
            StressProfile.MaxConcurrency,
            async i =>
            {
                // Soft save a cart, drive it to Review server-side, then soft save an updated review.
                (await Autosave(ids[i], StressOrder.DraftWithItems(i + 1)).ConfigureAwait(false))
                    .Should()
                    .BeOfType<AutosaveResult.Saved>();
                (await Advance(ids[i], "Next").ConfigureAwait(false))
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>();
                (await Autosave(ids[i], StressOrder.ReviewJson(i + 1, i + 2)).ConfigureAwait(false))
                    .Should()
                    .BeOfType<AutosaveResult.Saved>();
                Interlocked.Add(ref ops, 3);
                return 0;
            }
        );
        sw.Stop();

        var states = await Fan(n, StressProfile.MaxConcurrency, i => LoadState(ids[i]));
        states.Should().OnlyContain(s => s == "Review", "every instance ends in Review");

        TestContext.Progress.WriteLine(
            $"[throughput] {ops} ops ({n} instances x 3) in {sw.ElapsedMilliseconds} ms "
                + $"({ops * 1000.0 / Math.Max(1, sw.ElapsedMilliseconds):N0} ops/s)"
        );
    }
}
