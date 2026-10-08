using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.QueuedWorkListener;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Tests.Sqlite.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Sqlite.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Sqlite.Integration.IntegrationTests;

/// <summary>
/// On SQLite the dispatcher in the same process hears queued work once the save that queued it
/// commits, and hears nothing from a transaction that rolls back.
/// </summary>
/// <remarks>
/// The notice is raised on the committing thread before the commit returns, so whether one is
/// waiting can be read synchronously from the subscription's next wait, with no delay to guess.
/// </remarks>
[TestFixture]
public class SqliteQueuedWorkNoticeTests : TestSetup
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task A_queued_run_notifies_a_subscriber_in_the_same_process()
    {
        var listener = Scope.ServiceProvider.GetRequiredService<IQueuedWorkListener>();
        await using var subscription = await listener.SubscribeAsync(CancellationToken.None);

        await QueueAsync();

        await subscription.WaitAsync(CancellationToken.None).WaitAsync(Timeout);
    }

    [Test]
    public async Task A_rolled_back_enqueue_sends_no_notice()
    {
        var listener = Scope.ServiceProvider.GetRequiredService<IQueuedWorkListener>();
        await using var subscription = await listener.SubscribeAsync(CancellationToken.None);

        using (var transaction = await DataContext.BeginTransaction())
        {
            await DataContext.Track(
                WorkQueue.Create(
                    new CreateWorkQueue
                    {
                        TrainName = typeof(ISchedulerTestTrain).FullName!,
                        Input = "{}",
                        InputTypeName = typeof(SchedulerTestInput).FullName,
                    }
                )
            );
            await DataContext.SaveChanges(CancellationToken.None);
            await DataContext.RollbackTransaction();
        }
        DataContext.Reset();

        var next = subscription.WaitAsync(CancellationToken.None);
        next.IsCompleted.Should().BeFalse("a rolled back enqueue queued nothing");

        await QueueAsync();

        await next.WaitAsync(Timeout);
    }

    private async Task QueueAsync()
    {
        var execution = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        await execution.QueueAsync(
            typeof(ISchedulerTestTrain).FullName!,
            "{}",
            ct: CancellationToken.None
        );
    }
}
