using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Enums;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A job the dispatcher delivered to the local worker pool waits in <c>trax.background_job</c>
/// until a worker is free. The stale-pending reaper is a safety net for a job that was never
/// delivered, so a delivered job that is only waiting its turn must not be failed by it.
/// </summary>
[TestFixture]
public class StalePendingReaperDeliveredJobTests : TestSetup
{
    [Test]
    public async Task A_pending_run_whose_job_is_waiting_in_the_background_job_table_is_not_reaped()
    {
        var config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        var previous = config.StalePendingTimeout;
        config.StalePendingTimeout = TimeSpan.FromMinutes(20);

        try
        {
            var manifest = await CreateManifest();

            // Dispatched 25 minutes ago, and its job is sitting unclaimed in the local worker
            // queue because every worker is busy with earlier jobs.
            var metadata = await CreatePendingMetadata(manifest, DateTime.UtcNow.AddMinutes(-25));
            var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadata.Id });
            await DataContext.Track(job);
            await DataContext.SaveChanges(CancellationToken.None);
            DataContext.Reset();

            var train = Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
            await train.Run(Unit.Default);

            DataContext.Reset();
            var loaded = await DataContext
                .Metadatas.AsNoTracking()
                .FirstAsync(m => m.Id == metadata.Id);

            loaded
                .TrainState.Should()
                .Be(
                    TrainState.Pending,
                    "the job was delivered and is queued for a local worker; failing it means the "
                        + "worker later refuses it as no longer Pending and it never runs"
                );
        }
        finally
        {
            config.StalePendingTimeout = previous;
        }
    }

    private async Task<Manifest> CreateManifest()
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
                IntervalSeconds = 3600,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "delivered" },
            }
        );
        manifest.ManifestGroupId = group.Id;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }

    private async Task<Metadata> CreatePendingMetadata(Manifest manifest, DateTime startTime)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ManifestId = manifest.Id,
            }
        );

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);

        await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.StartTime, startTime));

        DataContext.Reset();
        return metadata;
    }
}
