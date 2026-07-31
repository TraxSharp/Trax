using System.Diagnostics;
using FluentAssertions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Tests.Stress.Fakes;
using Trax.Effect.StateMachine.Tests.Stress.Fixtures;

namespace Trax.Effect.StateMachine.Tests.Stress.IntegrationTests;

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
        await using var ctx = StressDb.NewContext();
        return await StressOrder.Service(new EfSnapshotStore(ctx)).Autosave("u", id, snapshot);
    }

    private static async Task<AdvanceOutcome> Advance(Guid id, string trigger)
    {
        await using var ctx = StressDb.NewContext();
        return await StressOrder.Service(new EfSnapshotStore(ctx)).Advance("u", id, trigger);
    }

    private static async Task<string> LoadState(Guid id)
    {
        await using var ctx = StressDb.NewContext();
        var loaded = await StressOrder.Service(new EfSnapshotStore(ctx)).Load("u", id);
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
                (await Autosave(ids[i], StressOrder.DraftWithItems(i + 1)))
                    .Should()
                    .BeOfType<AutosaveResult.Saved>();
                (await Advance(ids[i], "Next")).Should().BeOfType<AdvanceOutcome.Advanced>();
                (await Autosave(ids[i], StressOrder.ReviewJson(i + 1, i + 2)))
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
