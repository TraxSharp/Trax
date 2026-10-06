using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A retry's due time is compared with the database's clock when the dispatcher loads the queue
/// (<c>scheduled_at &lt;= now()</c>), so it is written by that clock too. A retry with no backoff
/// stores no time at all, and is dispatched on the very next poll whichever clock is ahead.
/// </summary>
/// <remarks>
/// That a backoff is written by the database's clock is tested in
/// <see cref="DatabaseClockSkewTests"/>, against a database clock skewed from this process's.
/// </remarks>
[TestFixture]
public class RetryDueTimeClockTests : TestSetup
{
    private SchedulerConfiguration _config = null!;
    private TimeSpan _previousDelay;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        _previousDelay = _config.DefaultRetryDelay;
    }

    [TearDown]
    public void RestoreDelay() => _config.DefaultRetryDelay = _previousDelay;

    [Test]
    public async Task A_retry_with_no_backoff_stores_no_time_and_dispatches_on_the_next_poll()
    {
        _config.DefaultRetryDelay = TimeSpan.Zero;
        var manifest = await CreateManifestWhoseLastRunFailed();

        await RunManifestManager();

        var entry = await QueuedEntryOf(manifest);
        entry.ScheduledAt.Should().BeNull("a retry due now waits on no clock");

        await Scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>().Run(Unit.Default);
        DataContext.Reset();

        (await DataContext.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == entry.Id))
            .Status.Should()
            .Be(WorkQueueStatus.Dispatched);
    }

    private async Task<Trax.Effect.Models.WorkQueue.WorkQueue> QueuedEntryOf(Manifest manifest) =>
        await DataContext
            .WorkQueues.AsNoTracking()
            .SingleAsync(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued);

    private async Task RunManifestManager()
    {
        await Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>().Run(Unit.Default);
        DataContext.Reset();
    }

    /// <summary>An interval manifest due now whose latest run failed an hour ago.</summary>
    private async Task<Manifest> CreateManifestWhoseLastRunFailed()
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "clock" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        manifest.LastSuccessfulRun = DateTime.UtcNow.AddMinutes(-5);
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "clock" },
                ManifestId = manifest.Id,
            }
        );
        run.TrainState = TrainState.Failed;
        run.StartTime = DateTime.UtcNow.AddHours(-1);
        run.EndTime = run.StartTime.AddMinutes(1);
        await DataContext.Track(run);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }
}
