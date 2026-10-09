using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// At most one queued entry replays a given run's decisions, held by
/// <c>ix_work_queue_unique_queued_replay</c>, so a requeue and a retry that race cannot both queue
/// a replay of the same run. An entry no longer queued does not count.
/// </summary>
[TestFixture]
public class QueuedReplayUniquenessTests : TestSetup
{
    private static WorkQueue Replaying(long source) =>
        WorkQueue.Create(
            new CreateWorkQueue { TrainName = "Replay.Unique.Train", ReplayDecisionsOf = source }
        );

    [Test]
    public async Task A_second_queued_replay_of_one_run_is_refused()
    {
        var source = Random.Shared.NextInt64(1_000_000_000, long.MaxValue);
        using var context = (IDataContext)DataContextFactory.Create();
        await context.Track(Replaying(source));
        await context.SaveChanges(CancellationToken.None);

        await context.Track(Replaying(source));
        var second = () => context.SaveChanges(CancellationToken.None);

        await second.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    [LeavesStuckRuns(
        "marks an entry dispatched by hand to exercise the partial index, with no run behind it"
    )]
    public async Task A_dispatched_replay_does_not_count_against_a_queued_one()
    {
        var source = Random.Shared.NextInt64(1_000_000_000, long.MaxValue);
        using var context = (IDataContext)DataContextFactory.Create();
        var dispatched = Replaying(source);
        dispatched.Status = WorkQueueStatus.Dispatched;
        await context.Track(dispatched);
        await context.Track(Replaying(source));

        var act = () => context.SaveChanges(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
