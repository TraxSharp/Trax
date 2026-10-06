using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Sqlite.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Sqlite.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher.Junctions;

namespace Trax.Scheduler.Tests.Sqlite.Integration.IntegrationTests;

/// <summary>
/// A disabled manifest pauses its scheduled work, and the dispatch query is what decides it: an
/// entry someone asked for by name still runs, and the held entries take no place in a group's
/// share of a cycle.
/// </summary>
[TestFixture]
public class SqliteDisabledManifestDispatchTests : TestSetup
{
    private DateTime _createdAt = DateTime.UtcNow.AddMinutes(-10);

    [Test]
    public async Task Entries_held_by_disabled_manifests_do_not_take_the_groups_share_of_a_cycle()
    {
        var config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        var original = config.MaxQueuedJobsPerCycle;
        config.MaxQueuedJobsPerCycle = 2;
        try
        {
            var group = await CreateAndSaveManifestGroup(
                DataContext,
                name: $"g-{Guid.NewGuid():N}"
            );
            var held = new List<long>();
            for (var i = 0; i < 2; i++)
            {
                var disabled = await SaveManifestAsync($"off{i}", isEnabled: false, group.Id);
                await QueueScheduledEntryAsync(disabled.Id);
                held.Add(disabled.Id);
            }
            var enabled = await SaveManifestAsync("on", isEnabled: true, group.Id);
            await QueueScheduledEntryAsync(enabled.Id);

            var loaded = await LoadQueuedAsync();

            loaded
                .Select(e => e.ManifestId)
                .Should()
                .Contain(enabled.Id, "the enabled manifest's entry is due")
                .And.NotContain(held.Cast<long?>(), "a disabled manifest's schedule is paused");
        }
        finally
        {
            config.MaxQueuedJobsPerCycle = original;
        }
    }

    [TestCase(true, TestName = "A trigger on a disabled manifest is loaded (group-fair load)")]
    [TestCase(false, TestName = "A trigger on a disabled manifest is loaded (load all queued)")]
    public async Task A_trigger_on_a_disabled_manifest_is_loaded_for_dispatch(bool groupFair)
    {
        var config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        var original = config.MaxQueuedJobsPerCycle;
        config.MaxQueuedJobsPerCycle = groupFair ? 100 : null;
        try
        {
            var group = await CreateAndSaveManifestGroup(
                DataContext,
                name: $"g-{Guid.NewGuid():N}"
            );
            var disabled = await SaveManifestAsync("off", isEnabled: false, group.Id);

            await Scope
                .ServiceProvider.GetRequiredService<ITraxScheduler>()
                .TriggerAsync(disabled.ExternalId);
            DataContext.Reset();

            var entry = await DataContext
                .WorkQueues.AsNoTracking()
                .SingleAsync(q =>
                    q.ManifestId == disabled.Id && q.Status == WorkQueueStatus.Queued
                );
            entry.IsExplicitTrigger.Should().BeTrue();

            (await LoadQueuedAsync())
                .Select(e => e.ManifestId)
                .Should()
                .Contain(disabled.Id, "an operator asked for this run by name");
        }
        finally
        {
            config.MaxQueuedJobsPerCycle = original;
        }
    }

    [Test]
    public async Task A_trigger_releases_the_entry_a_disabled_manifest_already_holds()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, name: $"g-{Guid.NewGuid():N}");
        var disabled = await SaveManifestAsync("off", isEnabled: false, group.Id);
        await QueueScheduledEntryAsync(disabled.Id);
        (await LoadQueuedAsync()).Select(e => e.ManifestId).Should().NotContain(disabled.Id);

        await Scope
            .ServiceProvider.GetRequiredService<ITraxScheduler>()
            .TriggerAsync(disabled.ExternalId);
        DataContext.Reset();

        (
            await DataContext.WorkQueues.CountAsync(q =>
                q.ManifestId == disabled.Id && q.Status == WorkQueueStatus.Queued
            )
        )
            .Should()
            .Be(1, "the held entry is the run the trigger asked for");
        (await LoadQueuedAsync())
            .Select(e => e.ManifestId)
            .Should()
            .Contain(disabled.Id, "the trigger released the held entry");
    }

    private Task<List<WorkQueue>> LoadQueuedAsync() =>
        ActivatorUtilities
            .CreateInstance<LoadQueuedJobsJunction>(Scope.ServiceProvider)
            .Run(Unit.Default);

    private async Task QueueScheduledEntryAsync(long manifestId)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                Input = "{}",
                InputTypeName = typeof(SchedulerTestInput).FullName,
                ManifestId = manifestId,
            }
        );
        // Distinct created_at values in the order queued, so the held entries rank ahead of the
        // enabled one.
        entry.CreatedAt = _createdAt = _createdAt.AddSeconds(1);
        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    private async Task<Manifest> SaveManifestAsync(string value, bool isEnabled, long groupId)
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = isEnabled,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = value },
            }
        );
        manifest.ManifestGroupId = groupId;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }
}
