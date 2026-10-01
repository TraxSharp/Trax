using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Scheduler.Services.DormantDependentContext;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// "Disabled jobs remain in the database but are skipped by the ManifestManager until
/// re-enabled" (scheduling-options). A disabled dormant dependent is a disabled job too.
/// </summary>
[TestFixture]
public class DisabledManifestStaysStoppedTests : TestSetup
{
    [TestCase(false, true, TestName = "Activating_a_disabled_dormant_dependent_queues_nothing")]
    [TestCase(
        true,
        false,
        TestName = "Activating_a_dormant_dependent_in_a_disabled_group_queues_nothing"
    )]
    public async Task Activating_a_stopped_dormant_dependent_queues_nothing(
        bool manifestEnabled,
        bool groupEnabled
    )
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var parent = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "parent" },
            }
        );
        parent.ManifestGroupId = group.Id;
        await DataContext.Track(parent);
        await DataContext.SaveChanges(CancellationToken.None);

        var dormant = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = manifestEnabled,
                ScheduleType = ScheduleType.DormantDependent,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "dormant" },
                DependsOnManifestId = parent.Id,
            }
        );
        var dormantGroup = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"dormant-group-{Guid.NewGuid():N}",
            isEnabled: groupEnabled
        );
        dormant.ManifestGroupId = dormantGroup.Id;
        dormant.ExternalId = "disabled-dormant";
        await DataContext.Track(dormant);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var context = Scope.ServiceProvider.GetRequiredService<DormantDependentContext>();
        context.Initialize(parent.Id);
        try
        {
            await context.ActivateAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                "disabled-dormant",
                new SchedulerTestInput { Value = "runtime" }
            );
        }
        finally
        {
            context.Reset();
        }

        DataContext.Reset();
        (await DataContext.WorkQueues.AnyAsync(q => q.ManifestId == dormant.Id))
            .Should()
            .BeFalse("the operator disabled this manifest or its group");
    }

    [Test]
    public async Task A_queued_retry_of_a_manifest_disabled_before_it_was_due_is_not_dispatched()
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = false,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "paused" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);

        // A retry the ManifestManager queued with a backoff before the operator disabled it.
        var entry = Trax.Effect.Models.WorkQueue.WorkQueue.Create(
            new Trax.Effect.Models.WorkQueue.DTOs.CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
            }
        );
        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        await Scope
            .ServiceProvider.GetRequiredService<Trax.Scheduler.Trains.JobDispatcher.IJobDispatcherTrain>()
            .Run(Unit.Default);

        DataContext.Reset();
        (await DataContext.Metadatas.AnyAsync(m => m.ManifestId == manifest.Id))
            .Should()
            .BeFalse("a disabled manifest is paused, and its queued retry is part of it");
    }

    [TestCase(true, TestName = "Group-fair load")]
    [TestCase(false, TestName = "Load all queued")]
    public async Task A_disabled_manifests_queued_entry_stays_queued_and_runs_once_re_enabled(
        bool groupFair
    )
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
        {
            s.MaxQueuedJobsPerCycle(groupFair ? 100 : null);
            s.Schedule<ISchedulerTestTrain>(
                "paused",
                new SchedulerTestInput { Value = "paused" },
                Every.Minutes(5)
            );
        });
        await fx.MaterializePendingManifestsAsync();
        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "paused");

        // The entry the ManifestManager queued before the operator disabled the manifest.
        var entry = Trax.Effect.Models.WorkQueue.WorkQueue.Create(
            new Trax.Effect.Models.WorkQueue.DTOs.CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
            }
        );
        await fx.DataContext.Track(entry);
        await fx.DataContext.SaveChanges(CancellationToken.None);
        fx.DataContext.Reset();

        await fx.Scheduler.DisableAsync("paused");
        await fx.RunJobDispatcherAsync();

        (await fx.DataContext.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == entry.Id))
            .Status.Should()
            .Be(WorkQueueStatus.Queued, "disabling pauses the entry rather than dropping it");

        await fx.Scheduler.EnableAsync("paused");
        await fx.RunJobDispatcherAsync();

        (await fx.DataContext.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == entry.Id))
            .Status.Should()
            .Be(WorkQueueStatus.Dispatched, "re-enabling the manifest releases its queued work");
    }

    [Test]
    public async Task A_dead_letter_requeue_of_a_disabled_manifest_still_runs()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "paused-dl",
                new SchedulerTestInput { Value = "x" },
                Every.Minutes(5)
            )
        );
        await fx.MaterializePendingManifestsAsync();
        var manifest = await fx
            .DataContext.Manifests.Include(m => m.ManifestGroup)
            .FirstAsync(m => m.ExternalId == "paused-dl");

        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "test",
                RetryCount = 1,
            }
        );
        await fx.DataContext.Track(deadLetter);
        await fx.DataContext.SaveChanges(CancellationToken.None);
        fx.DataContext.Reset();

        await fx.Scheduler.DisableAsync("paused-dl");
        (await fx.Scheduler.RequeueDeadLetterAsync(deadLetter.Id)).Success.Should().BeTrue();

        await fx.RunJobDispatcherAsync();

        (await fx.DataContext.Metadatas.AsNoTracking().AnyAsync(m => m.ManifestId == manifest.Id))
            .Should()
            .BeTrue("an operator asked for this run by name");
    }

    [TestCase(true, TestName = "A trigger on a disabled manifest runs (group-fair load)")]
    [TestCase(false, TestName = "A trigger on a disabled manifest runs (load all queued)")]
    public async Task A_trigger_on_a_disabled_manifest_runs(bool groupFair)
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
        {
            s.MaxQueuedJobsPerCycle(groupFair ? 100 : null);
            s.Schedule<ISchedulerTestTrain>(
                "paused-trigger",
                new SchedulerTestInput { Value = "x" },
                Every.Minutes(5)
            );
        });
        await fx.MaterializePendingManifestsAsync();
        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "paused-trigger");

        await fx.Scheduler.DisableAsync("paused-trigger");
        await fx.Scheduler.TriggerAsync("paused-trigger");

        await fx.RunJobDispatcherAsync();

        (await fx.DataContext.Metadatas.AsNoTracking().AnyAsync(m => m.ManifestId == manifest.Id))
            .Should()
            .BeTrue("an operator asked for this run by name");
    }
}
