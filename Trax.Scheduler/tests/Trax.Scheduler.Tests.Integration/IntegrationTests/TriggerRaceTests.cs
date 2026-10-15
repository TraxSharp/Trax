using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A trigger reads the manifest's queued entry and, finding none, inserts its own. The ManifestManager can queue an
/// entry between the two, and the dispatcher can take that entry before the trigger reads it again.
/// </summary>
[TestFixture]
public class TriggerRaceTests : TestSetup
{
    [Test]
    public async Task A_trigger_whose_collision_was_dispatched_queues_a_new_entry()
    {
        var scheduler = (TraxScheduler)Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
        var manifest = await scheduler.ScheduleAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            "trigger-race",
            new SchedulerTestInput { Value = "race" },
            Every.Minutes(5)
        );

        long? collided = null;
        // The ManifestManager queues an entry after the trigger found none, and the dispatcher
        // takes it after the trigger's insert met it and before the trigger reads it again.
        scheduler.BeforeTriggerInsert = async (attempt, ct) =>
        {
            if (attempt == 1)
                collided = await QueueEntryAsync(manifest, ct);
        };
        scheduler.AfterTriggerCollision = async (attempt, ct) =>
        {
            if (attempt == 1)
                await DispatchAsync(manifest, collided!.Value, ct);
        };

        var result = await scheduler.TriggerAsync(manifest.ExternalId, askAfresh: false);

        collided.Should().NotBeNull();
        result.Created.Should().BeTrue("the entry it met was dispatched, so its place was free");
        result.AlreadyDispatched.Should().BeFalse();
        result.WorkQueueId.Should().NotBe(collided!.Value);
        DataContext.Reset();
        var queued = await DataContext
            .WorkQueues.AsNoTracking()
            .SingleAsync(w => w.ManifestId == manifest.Id && w.Status == WorkQueueStatus.Queued);
        queued.Id.Should().Be(result.WorkQueueId);
        queued.IsExplicitTrigger.Should().BeTrue();
    }

    [Test]
    public async Task A_trigger_colliding_on_every_attempt_throws_the_unique_violation()
    {
        var manifest = await ScheduleAsync("trigger-race-every");
        var inserts = 0;
        long collided = 0;
        var seams = new TraxScheduler.TriggerSeams(
            null,
            async (_, ct) =>
            {
                inserts++;
                collided = await QueueEntryAsync(manifest, ct);
            },
            (_, ct) => DispatchAsync(manifest, collided, ct)
        );

        var trigger = () =>
            TraxScheduler.TriggerManifestAsync(
                DataContext,
                manifest,
                DateTime.UtcNow,
                askAfresh: false,
                seams,
                CancellationToken.None
            );

        await trigger.Should().ThrowAsync<DbUpdateException>();
        inserts.Should().Be(3, "a trigger inserts a few times at most");
        ((DbContext)DataContext)
            .ChangeTracker.Entries<WorkQueue>()
            .Should()
            .BeEmpty(
                "the entry that met the queued one is untracked, so a later save does not retry it"
            );
    }

    [Test]
    public async Task A_non_conflict_update_failure_is_not_retried()
    {
        var manifest = await ScheduleAsync("trigger-race-other");
        var inserts = 0;
        var seams = new TraxScheduler.TriggerSeams(
            null,
            (_, _) =>
            {
                inserts++;
                throw new DbUpdateException("not a queued-manifest conflict");
            },
            null
        );

        var trigger = () =>
            TraxScheduler.TriggerManifestAsync(
                DataContext,
                manifest,
                DateTime.UtcNow,
                askAfresh: false,
                seams,
                CancellationToken.None
            );

        await trigger
            .Should()
            .ThrowAsync<DbUpdateException>()
            .WithMessage("not a queued-manifest conflict");
        inserts.Should().Be(1, "only a conflict on the manifest's queued entry is tried again");
    }

    [Test]
    public async Task A_non_conflict_update_failure_leaves_nothing_tracked()
    {
        var manifest = await ScheduleAsync("trigger-race-untracked");
        DataContext.Reset();
        var seams = new TraxScheduler.TriggerSeams(
            null,
            (_, _) => throw new DbUpdateException("not a queued-manifest conflict"),
            null
        );

        var trigger = () =>
            TraxScheduler.TriggerManifestAsync(
                DataContext,
                manifest,
                DateTime.UtcNow,
                askAfresh: false,
                seams,
                CancellationToken.None
            );

        await trigger.Should().ThrowAsync<DbUpdateException>();
        ((DbContext)DataContext)
            .ChangeTracker.Entries<WorkQueue>()
            .Should()
            .BeEmpty(
                "the entry that failed to save is untracked, so a later save does not insert it"
            );
    }

    private async Task<Manifest> ScheduleAsync(string externalId) =>
        await (
            (TraxScheduler)Scope.ServiceProvider.GetRequiredService<ITraxScheduler>()
        ).ScheduleAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            externalId,
            new SchedulerTestInput { Value = "race" },
            Every.Minutes(5)
        );

    private async Task<long> QueueEntryAsync(Manifest manifest, CancellationToken ct)
    {
        await using var db = await Scope
            .ServiceProvider.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(ct);
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
            }
        );
        await db.Track(entry);
        await db.SaveChanges(ct);
        return entry.Id;
    }

    // As the dispatcher does: the entry moves to Dispatched, linked to the run it started.
    private async Task DispatchAsync(Manifest manifest, long entryId, CancellationToken ct)
    {
        await using var db = await Scope
            .ServiceProvider.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(ct);
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = manifest.Name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ManifestId = manifest.Id,
            }
        );
        run.TrainState = TrainState.Completed;
        run.EndTime = DateTime.UtcNow;
        await db.Track(run);
        await db.SaveChanges(ct);
        await db
            .WorkQueues.Where(w => w.Id == entryId)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(w => w.Status, WorkQueueStatus.Dispatched)
                        .SetProperty(w => w.MetadataId, run.Id)
                        .SetProperty(w => w.DispatchedAt, DateTime.UtcNow),
                ct
            );
    }
}
