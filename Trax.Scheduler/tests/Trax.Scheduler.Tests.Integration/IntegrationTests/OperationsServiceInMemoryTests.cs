using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ChangeSignal;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// The operations surface's writes on the InMemory provider, which has no set-based
/// <c>ExecuteUpdate</c>. A host on InMemory (tests, small samples) gets the same result a
/// relational host gets from one statement, by loading and saving the rows that change.
/// The relational path is covered by <see cref="OperationsServiceBatchTests"/> and
/// <see cref="TraxSchedulerCancelTests"/>.
///
/// <para>Enforces <c>docs/adr/0007-the-operations-surface-runs-on-inmemory.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0007-the-operations-surface-runs-on-inmemory.md")]
[TestFixture]
public class OperationsServiceInMemoryTests
{
    private RecordingChangeSignal _signal = null!;
    private SchedulerE2EFixture _fx = null!;
    private IOperationsService _operations = null!;

    [SetUp]
    public void SetUp()
    {
        _signal = new RecordingChangeSignal();
        _fx = SchedulerE2EFixture.CreateInMemory(
            _ => { },
            services => services.AddSingleton<ITraxChangeSignal>(_signal)
        );
        _operations = _fx.Services.GetRequiredService<IOperationsService>();
    }

    [TearDown]
    public async Task TearDown() => await _fx.DisposeAsync();

    [Test]
    public async Task Cancel_executions_flags_pending_and_in_progress_runs_and_skips_the_rest()
    {
        var pending = await SeedRun(TrainState.Pending);
        var running = await SeedRun(TrainState.InProgress);
        var completed = await SeedRun(TrainState.Completed);

        var result = await _operations.CancelExecutionsAsync(
            [pending.Id, running.Id, completed.Id],
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result
            .Count.Should()
            .Be(
                2,
                "the operations surface runs on InMemory (docs/adr/0007-the-operations-surface-runs-on-inmemory.md)"
            );
        (await Flagged()).Should().BeEquivalentTo([pending.Id, running.Id]);
        _signal.Domains.Should().Equal(ChangeDomain.Execution);
    }

    [Test]
    public async Task Cancel_work_queue_entries_cancels_only_queued_entries()
    {
        var queued = await SeedEntry(WorkQueueStatus.Queued);
        var dispatched = await SeedEntry(WorkQueueStatus.Dispatched);

        var result = await _operations.CancelWorkQueueEntriesAsync(
            [queued.Id, dispatched.Id],
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Count.Should().Be(1);
        var statuses = await _fx
            .DataContext.WorkQueues.AsNoTracking()
            .ToDictionaryAsync(q => q.Id, q => q.Status);
        statuses[queued.Id].Should().Be(WorkQueueStatus.Cancelled);
        statuses[dispatched.Id].Should().Be(WorkQueueStatus.Dispatched);
        _signal.Domains.Should().Equal(ChangeDomain.WorkQueue);
    }

    [Test]
    public async Task Set_manifests_enabled_writes_only_those_that_differ()
    {
        var group = await SeedGroup("g", enabled: true);
        var on = await SeedManifest(group, enabled: true);
        var off = await SeedManifest(group, enabled: false);

        var result = await _operations.SetManifestsEnabledAsync(
            [on.Id, off.Id],
            false,
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Count.Should().Be(1, "only the enabled one changed");
        (await _fx.DataContext.Manifests.AsNoTracking().AnyAsync(m => m.IsEnabled))
            .Should()
            .BeFalse();
        _signal.Domains.Should().Equal(ChangeDomain.Manifest);
    }

    [Test]
    public async Task Set_manifest_groups_enabled_writes_the_listed_groups_and_bumps_updated_at()
    {
        var target = await SeedGroup("target", enabled: true);
        var other = await SeedGroup("other", enabled: true);

        var result = await _operations.SetManifestGroupsEnabledAsync(
            [target.Id],
            false,
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Count.Should().Be(1);
        var fresh = await _fx
            .DataContext.ManifestGroups.AsNoTracking()
            .SingleAsync(g => g.Id == target.Id);
        fresh.IsEnabled.Should().BeFalse();
        fresh.UpdatedAt.Should().BeAfter(target.UpdatedAt);
        (await _fx.DataContext.ManifestGroups.AsNoTracking().SingleAsync(g => g.Id == other.Id))
            .IsEnabled.Should()
            .BeTrue();
        _signal.Domains.Should().Equal(ChangeDomain.ManifestGroup);
    }

    [Test]
    public async Task Set_all_manifest_groups_enabled_writes_every_group_that_differs()
    {
        await SeedGroup("a", enabled: true);
        await SeedGroup("b", enabled: true);
        await SeedGroup("c", enabled: false);

        var result = await _operations.SetAllManifestGroupsEnabledAsync(
            false,
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Count.Should().Be(2);
        (await _fx.DataContext.ManifestGroups.AsNoTracking().AnyAsync(g => g.IsEnabled))
            .Should()
            .BeFalse();
    }

    [Test]
    public async Task Scheduler_cancel_flags_a_manifests_pending_and_in_progress_runs()
    {
        var group = await SeedGroup("cancel", enabled: true);
        var manifest = await SeedManifest(group, enabled: true);
        var running = await SeedRun(TrainState.InProgress, manifest.Id);
        await SeedRun(TrainState.Completed, manifest.Id);

        var flagged = await _fx.Scheduler.CancelAsync(manifest.ExternalId);

        flagged.Should().Be(1);
        (await Flagged()).Should().Equal(running.Id);
        _signal.Domains.Should().Equal(ChangeDomain.Execution);
    }

    [Test]
    public async Task Scheduler_cancel_group_flags_the_groups_pending_and_in_progress_runs()
    {
        var group = await SeedGroup("cancel-group", enabled: true);
        var other = await SeedGroup("untouched", enabled: true);
        var pending = await SeedRun(
            TrainState.Pending,
            (await SeedManifest(group, enabled: true)).Id
        );
        await SeedRun(TrainState.InProgress, (await SeedManifest(other, enabled: true)).Id);

        var flagged = await _fx.Scheduler.CancelGroupAsync(group.Id);

        flagged.Should().Be(1);
        (await Flagged()).Should().Equal(pending.Id);
    }

    [Test]
    public async Task A_scheduler_trigger_releases_a_queued_entry_without_a_set_update()
    {
        var group = await SeedGroup("trigger", enabled: true);
        var queued = await SeedManifest(group, enabled: true);
        var waiting = await Save(
            WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = queued.Name,
                    Input = queued.Properties,
                    InputTypeName = queued.PropertyTypeName,
                    ManifestId = queued.Id,
                    ScheduledAt = DateTime.UtcNow.AddHours(1),
                }
            )
        );

        var result = await _fx.Scheduler.TriggerAsync(
            queued.ExternalId,
            askAfresh: false,
            CancellationToken.None
        );

        result.Created.Should().BeFalse();
        result.MovedForward.Should().BeTrue();
        var released = await _fx
            .DataContext.WorkQueues.AsNoTracking()
            .SingleAsync(w => w.Id == waiting.Id);
        released
            .IsExplicitTrigger.Should()
            .BeTrue(
                "the operations surface runs on InMemory (docs/adr/0007-the-operations-surface-runs-on-inmemory.md)"
            );
    }

    [Test]
    public async Task Batch_group_cancel_acts_on_every_given_group()
    {
        var first = await SeedGroup("first", enabled: true);
        var second = await SeedGroup("second", enabled: true);
        var a = await SeedManifest(first, enabled: true);
        await SeedManifest(second, enabled: true);
        var running = await SeedRun(TrainState.InProgress, a.Id);

        var cancelled = await _operations.CancelManifestGroupsAsync(
            [first.Id, second.Id],
            CancellationToken.None
        );

        cancelled.Count.Should().Be(1);
        (await Flagged()).Should().Equal(running.Id);
    }

    [Test]
    public async Task Logs_filter_by_text_and_read_oldest_first()
    {
        foreach (
            var (message, category) in new[]
            {
                ("Disk at 50% capacity", "App.Storage"),
                ("Disk at 500 MB", "App.Storage"),
                ("connected to broker", "App.Messaging"),
                ("DISK alarm cleared", "app.storage.alarms"),
            }
        )
            await Save(
                Trax.Effect.Models.Log.Log.Create(
                    new Trax.Effect.Models.Log.DTOs.CreateLog
                    {
                        Level = Microsoft.Extensions.Logging.LogLevel.Information,
                        Message = message,
                        CategoryName = category,
                        EventId = 0,
                    }
                )
            );

        var page = await _operations.GetLogsAsync(
            new LogQuery
            {
                MessageContains = "disk",
                CategoryContains = "STORAGE",
                Order = LogOrder.OldestFirst,
            },
            CancellationToken.None
        );
        var percent = await _operations.CountLogsAsync(
            new LogQuery { MessageContains = "50%" },
            CancellationToken.None
        );

        page.Items.Select(l => l.Message)
            .Should()
            .Equal("Disk at 50% capacity", "Disk at 500 MB", "DISK alarm cleared");
        percent.Should().Be(1, "% in the term matches itself, not any text");
    }

    [TestCase(OperationsService.LogCountCap, OperationsService.LogCountCap, false)]
    [TestCase(OperationsService.LogCountCap + 1, OperationsService.LogCountCap, true)]
    public async Task A_text_filtered_log_count_stops_at_the_cap(
        int matching,
        int expectedCount,
        bool expectedCapped
    )
    {
        for (var i = 0; i < matching; i++)
            await _fx.DataContext.Track(
                Trax.Effect.Models.Log.Log.Create(
                    new Trax.Effect.Models.Log.DTOs.CreateLog
                    {
                        Level = Microsoft.Extensions.Logging.LogLevel.Information,
                        Message = $"capped line {i}",
                        CategoryName = "App.Capped",
                        EventId = 0,
                    }
                )
            );
        await _fx.DataContext.SaveChanges(CancellationToken.None);
        _fx.DataContext.Reset();

        (
            await _operations.CountLogsCappedAsync(
                new LogQuery { MessageContains = "CAPPED LINE" },
                CancellationToken.None
            )
        )
            .Should()
            .Be(new LogCount(expectedCount, expectedCapped));
        (
            await _operations.CountLogsCappedAsync(
                new LogQuery(Category: "App.Capped"),
                CancellationToken.None
            )
        )
            .Should()
            .Be(new LogCount(matching, Capped: false), "a count without a text filter is exact");
    }

    [Test]
    public async Task Dashboard_metrics_average_root_run_durations()
    {
        var start = DateTime.UtcNow.AddMinutes(-30);
        foreach (var seconds in new[] { 10, 20 })
        {
            var run = await SeedRun(TrainState.Completed);
            run.StartTime = start;
            run.EndTime = start.AddSeconds(seconds);
            _fx.DataContext.Metadatas.Update(run);
        }
        await _fx.DataContext.SaveChanges(CancellationToken.None);
        _fx.DataContext.Reset();

        var metrics = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last60Minutes,
            hideAdminTrains: false,
            CancellationToken.None
        );

        metrics.TopAverageDurations.Should().ContainSingle();
        metrics.TopAverageDurations[0].AverageMilliseconds.Should().BeApproximately(15000, 1);
        metrics.ExecutionsOverTime.Sum(b => b.Completed).Should().Be(2);
    }

    #region Helpers

    private async Task<List<long>> Flagged() =>
        await _fx
            .DataContext.Metadatas.AsNoTracking()
            .Where(m => m.CancellationRequested)
            .Select(m => m.Id)
            .ToListAsync();

    private async Task<T> Save<T>(T entity)
        where T : class, Trax.Effect.Models.IModel
    {
        await _fx.DataContext.Track(entity);
        await _fx.DataContext.SaveChanges(CancellationToken.None);
        _fx.DataContext.Reset();
        _signal.Clear();
        return entity;
    }

    private Task<ManifestGroup> SeedGroup(string name, bool enabled) =>
        Save(
            new ManifestGroup
            {
                Name = $"{name}-{Guid.NewGuid():N}",
                IsEnabled = enabled,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-1),
            }
        );

    private Task<Manifest> SeedManifest(ManifestGroup group, bool enabled)
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = enabled,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                Properties = new SchedulerTestInput { Value = "in-memory" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        return Save(manifest);
    }

    private Task<Metadata> SeedRun(TrainState state, long? manifestId = null)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ManifestId = manifestId,
            }
        );
        metadata.TrainState = state;
        return Save(metadata);
    }

    private Task<WorkQueue> SeedEntry(WorkQueueStatus status)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                Input = null,
                InputTypeName = null,
            }
        );
        entry.Status = status;
        return Save(entry);
    }

    #endregion
}
