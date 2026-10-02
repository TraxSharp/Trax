using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobDispatcher.Junctions;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A queued entry whose stored JSON no longer fits its type is settled as a failed run instead
/// of staying queued. One whose input type this host does not register is left queued for a host
/// that does, and settled only after enough claims have found no such host.
/// </summary>
[TestFixture]
public class UnreadableQueuedInputTests : TestSetup
{
    private const string UnknownType = "Some.Other.Host.NewTrainInput";

    [Test]
    public async Task An_entry_whose_input_type_this_host_does_not_register_stays_queued_for_another_host()
    {
        var subject = $"subject-{Guid.NewGuid():N}";
        var unknown = await Queue(subject, """{"value":"new"}""", UnknownType);
        var sibling = await Queue(
            subject,
            """{"value":"known"}""",
            typeof(SchedulerTestInput).FullName!
        );

        await RunDispatcher();
        await RunDispatcher();

        DataContext.Reset();
        var unknownRow = await DataContext
            .WorkQueues.AsNoTracking()
            .SingleAsync(q => q.Id == unknown.Id);
        unknownRow
            .Status.Should()
            .Be(WorkQueueStatus.Queued, "a host that registers the type may still claim it");
        unknownRow.MetadataId.Should().BeNull("no run is recorded against the manifest");
        unknownRow.ScheduledAt.Should().BeAfter(DateTime.UtcNow, "it is pushed back");
        unknownRow.DispatchAttempts.Should().Be(1);
        (await DataContext.Metadatas.CountAsync(m => m.ExternalId == unknown.ExternalId))
            .Should()
            .Be(0);

        var siblingRow = await DataContext
            .WorkQueues.AsNoTracking()
            .SingleAsync(q => q.Id == sibling.Id);
        siblingRow
            .Status.Should()
            .Be(WorkQueueStatus.Dispatched, "a deferred entry does not hold its subject");
    }

    [Test]
    public async Task An_entry_whose_input_type_no_host_registers_is_failed_after_the_last_attempt()
    {
        var entry = await Queue(subject: null, """{"value":"new"}""", UnknownType);
        await DataContext
            .WorkQueues.Where(q => q.Id == entry.Id)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(
                    q => q.DispatchAttempts,
                    DispatchJobsJunction.UnknownInputTypeMaxSkips - 1
                )
            );

        await RunDispatcher();

        DataContext.Reset();
        var row = await DataContext.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == entry.Id);
        row.Status.Should().Be(WorkQueueStatus.Dispatched);
        var run = await DataContext
            .Metadatas.AsNoTracking()
            .SingleAsync(m => m.Id == row.MetadataId);
        run.TrainState.Should().Be(TrainState.Failed);
        run.FailureReason.Should().Contain(UnknownType);
    }

    [Test]
    public async Task An_unreadable_dead_letter_retry_is_linked_to_its_failed_run()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, name: $"g-{Guid.NewGuid():N}");
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "dl" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);

        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "test",
                RetryCount = 3,
            }
        );
        await DataContext.Track(deadLetter);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                Input = """{"value": {"not": "a string"}}""",
                InputTypeName = typeof(SchedulerTestInput).FullName!,
                ManifestId = manifest.Id,
                DeadLetterId = deadLetter.Id,
            }
        );
        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        await RunDispatcher();

        DataContext.Reset();
        var row = await DataContext.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == entry.Id);
        row.MetadataId.Should().NotBeNull();
        var reloaded = await DataContext
            .DeadLetters.AsNoTracking()
            .SingleAsync(d => d.Id == deadLetter.Id);
        reloaded
            .RetryMetadataId.Should()
            .Be(row.MetadataId, "the retry's outcome is the failed run recorded for it");
    }

    [Test]
    public async Task An_input_whose_json_no_longer_fits_its_type_is_failed()
    {
        var entry = await Queue(
            subject: null,
            """{"value": {"not": "a string"}}""",
            typeof(SchedulerTestInput).FullName!
        );

        await RunDispatcher();

        DataContext.Reset();
        var row = await DataContext.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == entry.Id);
        row.Status.Should().Be(WorkQueueStatus.Dispatched);
        var run = await DataContext
            .Metadatas.AsNoTracking()
            .SingleAsync(m => m.Id == row.MetadataId);
        run.TrainState.Should().Be(TrainState.Failed);
        run.FailureReason.Should().Contain("could not be read");
    }

    [Test]
    public async Task An_unreadable_requeue_carries_its_replay_link_onto_its_failed_run()
    {
        // The failed run stands for the requeue, so a requeue of it in turn must still lead back
        // to the run whose decisions it was to replay.
        var source = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        await DataContext.Track(source);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        var entry = await Queue(
            subject: null,
            """{"value": {"not": "a string"}}""",
            typeof(SchedulerTestInput).FullName!,
            replayDecisionsOf: source.Id
        );

        await RunDispatcher();

        DataContext.Reset();
        var row = await DataContext.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == entry.Id);
        var run = await DataContext
            .Metadatas.AsNoTracking()
            .SingleAsync(m => m.Id == row.MetadataId);
        run.TrainState.Should().Be(TrainState.Failed);
        run.ReplayDecisionsOf.Should().Be(source.Id);
    }

    private async Task<WorkQueue> Queue(
        string? subject,
        string input,
        string inputTypeName,
        long? replayDecisionsOf = null
    )
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                Input = input,
                InputTypeName = inputTypeName,
                SubjectKey = subject,
                ReplayDecisionsOf = replayDecisionsOf,
            }
        );
        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return entry;
    }

    private async Task RunDispatcher()
    {
        using var scope = Scope.ServiceProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();
        await train.Run(Unit.Default);
    }
}
