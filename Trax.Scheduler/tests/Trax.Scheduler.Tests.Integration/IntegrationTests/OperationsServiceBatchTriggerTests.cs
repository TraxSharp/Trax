using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.EffectProvider;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// The dashboard's Trigger Selected (manifests and groups) and Cancel Running (groups) as one
/// operations-service call each, so the GraphQL API offers the same batch actions with the same
/// counts instead of either surface looping the single calls.
///
/// <para>Enforces <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>:
/// the batch logic lives here, not in a surface.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[TestFixture]
public class OperationsServiceBatchTriggerTests : TestSetup
{
    private RecordingChangeSignal _signal = null!;
    private OperationsService _operations = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _signal = new RecordingChangeSignal();
        _operations = new OperationsService(
            Substitute.For<ITrainDiscoveryService>(),
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>(),
            new SchedulerConfiguration(),
            Substitute.For<ITrainExecutionService>(),
            Scope.ServiceProvider,
            changeSignal: _signal
        );
    }

    #region Validation

    private static IEnumerable<TestCaseData> EveryTriggerBatch()
    {
        yield return new TestCaseData(
            (Func<OperationsService, IReadOnlyCollection<long>, Task<BatchTriggerResult>>)(
                (o, ids) => o.TriggerManifestsAsync(ids, askAfresh: false, CancellationToken.None)
            )
        ).SetArgDisplayNames("TriggerManifests");
        yield return new TestCaseData(
            (Func<OperationsService, IReadOnlyCollection<long>, Task<BatchTriggerResult>>)(
                (o, ids) => o.TriggerManifestGroupsAsync(ids, CancellationToken.None)
            )
        ).SetArgDisplayNames("TriggerManifestGroups");
    }

    [TestCaseSource(nameof(EveryTriggerBatch))]
    public async Task An_empty_list_triggers_nothing(
        Func<OperationsService, IReadOnlyCollection<long>, Task<BatchTriggerResult>> batch
    )
    {
        var result = await batch(_operations, []);

        result.Success.Should().BeFalse("an empty selection is a caller's mistake, not a no-op");
        result.Message.Should().Contain("No ids");
        _signal.Domains.Should().BeEmpty();
    }

    [TestCaseSource(nameof(EveryTriggerBatch))]
    public async Task A_list_over_the_batch_limit_triggers_nothing(
        Func<OperationsService, IReadOnlyCollection<long>, Task<BatchTriggerResult>> batch
    )
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        var manifest = await SeedManifest(group, enabled: true);
        var ids = Enumerable
            .Range(1, OperationsService.MaxBatchSize)
            .Select(i => (long)i + 10_000_000)
            .Append(manifest.Id)
            .Append(group.Id)
            .ToList();

        var result = await batch(_operations, ids);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(OperationsService.MaxBatchSize.ToString());
        (await QueuedFor(manifest.Id)).Should().Be(0);
        _signal.Domains.Should().BeEmpty();
    }

    [Test]
    public async Task Cancel_groups_refuses_an_empty_list_and_a_list_over_the_limit()
    {
        (await _operations.CancelManifestGroupsAsync([], CancellationToken.None))
            .Success.Should()
            .BeFalse();
        var tooMany = Enumerable.Range(1, OperationsService.MaxBatchSize + 1).Select(i => (long)i);
        var result = await _operations.CancelManifestGroupsAsync(
            tooMany.ToList(),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(OperationsService.MaxBatchSize.ToString());
    }

    #endregion

    #region TriggerManifestsAsync

    [Test]
    public async Task Three_manifests_one_already_queued_queue_two_and_report_one_already_queued()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        var a = await SeedManifest(group, enabled: true);
        var b = await SeedManifest(group, enabled: false);
        var c = await SeedManifest(group, enabled: true);
        var waiting = await SeedQueuedEntry(c, scheduledAt: DateTime.UtcNow.AddHours(1));

        var result = await _operations.TriggerManifestsAsync(
            [a.Id, b.Id, c.Id],
            askAfresh: false,
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Matched.Should().Be(3);
        result.Queued.Should().Be(2, "a and b had no queued entry; a disabled one still runs");
        result.AlreadyQueued.Should().Be(1, "c's entry becomes the triggered run");
        result.Skipped.Should().Be(0);
        result.Notes.Should().BeEmpty();
        result.Message.Should().Contain("2 queued").And.Contain("1 already queued");

        foreach (var id in new[] { a.Id, b.Id, c.Id })
            (await QueuedFor(id)).Should().Be(1, "a manifest holds at most one queued entry");

        var released = await DataContext
            .WorkQueues.AsNoTracking()
            .SingleAsync(w => w.Id == waiting.Id);
        released.IsExplicitTrigger.Should().BeTrue();
        released.ScheduledAt.Should().BeNull("it was brought forward to now");
        _signal.Domains.Should().Equal([ChangeDomain.WorkQueue], "the change is signalled once");
    }

    [Test]
    public async Task Unknown_ids_are_skipped_and_noted_without_stopping_the_rest()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        var known = await SeedManifest(group, enabled: true);

        var result = await _operations.TriggerManifestsAsync(
            [999_999, known.Id, known.Id],
            askAfresh: false,
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Matched.Should().Be(1, "a repeated id is triggered once");
        result.Queued.Should().Be(1);
        result.Skipped.Should().Be(1);
        result.Notes.Should().ContainSingle(n => n.Id == 999_999);
        (await QueuedFor(known.Id)).Should().Be(1);
    }

    [Test]
    public async Task Only_unknown_ids_signal_nothing()
    {
        var result = await _operations.TriggerManifestsAsync(
            [999_998, 999_999],
            askAfresh: false,
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Skipped.Should().Be(2);
        _signal.Domains.Should().BeEmpty();
    }

    [Test]
    public async Task Asking_afresh_releases_a_queued_retry_without_its_replay_link()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        var manifest = await SeedManifest(group, enabled: true);
        var failed = await SeedRun(manifest.Id, TrainState.Failed);
        var retry = await SeedQueuedEntry(
            manifest,
            scheduledAt: DateTime.UtcNow.AddMinutes(10),
            replayDecisionsOf: failed.Id
        );

        var result = await _operations.TriggerManifestsAsync(
            [manifest.Id],
            askAfresh: true,
            CancellationToken.None
        );

        result.AlreadyQueued.Should().Be(1);
        result.TooLateToAskAfresh.Should().Be(0);
        (await DataContext.WorkQueues.AsNoTracking().SingleAsync(w => w.Id == retry.Id))
            .ReplayDecisionsOf.Should()
            .BeNull("the trigger asked the retry's deciders afresh");
    }

    [Test]
    public async Task Without_asking_afresh_a_queued_retry_keeps_its_replay_link()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        var manifest = await SeedManifest(group, enabled: true);
        var failed = await SeedRun(manifest.Id, TrainState.Failed);
        var retry = await SeedQueuedEntry(
            manifest,
            scheduledAt: DateTime.UtcNow.AddMinutes(10),
            replayDecisionsOf: failed.Id
        );

        await _operations.TriggerManifestsAsync(
            [manifest.Id],
            askAfresh: false,
            CancellationToken.None
        );

        (await DataContext.WorkQueues.AsNoTracking().SingleAsync(w => w.Id == retry.Id))
            .ReplayDecisionsOf.Should()
            .Be(failed.Id);
    }

    [Test]
    public async Task Each_manifest_of_a_batch_is_saved_with_only_its_own_entry_tracked()
    {
        // A save checks every entity its context tracks. One context runs the whole batch, so
        // entries left tracked from earlier triggers would make every save slower than the last.
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        var ids = new List<long>();
        for (var i = 0; i < 4; i++)
            ids.Add((await SeedManifest(group, enabled: true)).Id);
        var trackedAtSave = new List<int>();
        var operations = new OperationsService(
            Substitute.For<ITrainDiscoveryService>(),
            new SaveObservingFactory(
                Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>(),
                context => trackedAtSave.Add(context.ChangeTracker.Entries().Count())
            ),
            new SchedulerConfiguration(),
            Substitute.For<ITrainExecutionService>(),
            Scope.ServiceProvider,
            changeSignal: _signal
        );

        var result = await operations.TriggerManifestsAsync(
            ids,
            askAfresh: false,
            CancellationToken.None
        );

        result.Queued.Should().Be(4, result.Message);
        trackedAtSave.Should().Equal([1, 1, 1, 1]);
    }

    #endregion

    #region TriggerManifestAsync

    [Test]
    public async Task A_single_trigger_says_it_queued_a_new_run()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        var manifest = await SeedManifest(group, enabled: true);

        var result = await _operations.TriggerManifestAsync(
            manifest.ExternalId,
            delay: null,
            askAfresh: false,
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Trigger!.Created.Should().BeTrue();
        result.Message.Should().Contain("queued a new run");
        (await QueuedFor(manifest.Id)).Should().Be(1);
        _signal.Domains.Should().Equal(ChangeDomain.WorkQueue);
    }

    [Test]
    public async Task A_trigger_whose_delay_overflows_the_due_time_is_refused()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        var manifest = await SeedManifest(group, enabled: true);

        var result = await _operations.TriggerManifestAsync(
            manifest.ExternalId,
            delay: TimeSpan.MaxValue,
            askAfresh: false,
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Trigger.Should().BeNull();
        result.Message.Should().Contain("past the latest time");
        (await QueuedFor(manifest.Id)).Should().Be(0);
    }

    [Test]
    public async Task A_single_trigger_says_it_brought_a_queued_run_forward()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        var manifest = await SeedManifest(group, enabled: true);
        var waiting = await SeedQueuedEntry(manifest, DateTime.UtcNow.AddHours(1));

        var result = await _operations.TriggerManifestAsync(
            manifest.ExternalId,
            delay: null,
            askAfresh: false,
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Trigger!.Created.Should().BeFalse();
        result.Trigger.MovedForward.Should().BeTrue();
        result.Trigger.WorkQueueId.Should().Be(waiting.Id);
        result.Message.Should().Contain("brought it forward");
        (await QueuedFor(manifest.Id)).Should().Be(1, "nothing more is queued");
    }

    [Test]
    public async Task A_single_trigger_says_an_entry_already_due_now_runs_as_the_trigger()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        var manifest = await SeedManifest(group, enabled: true);
        await SeedQueuedEntry(manifest, scheduledAt: null);

        var result = await _operations.TriggerManifestAsync(
            manifest.ExternalId,
            delay: TimeSpan.FromMinutes(5),
            askAfresh: false,
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Trigger!.Created.Should().BeFalse();
        result.Trigger.MovedForward.Should().BeFalse();
        result.Message.Should().Contain("now runs as the trigger");
    }

    [Test]
    public async Task A_single_trigger_of_an_unknown_external_id_changes_nothing()
    {
        var result = await _operations.TriggerManifestAsync(
            $"missing-{Guid.NewGuid():N}",
            delay: null,
            askAfresh: false,
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
        result.Trigger.Should().BeNull();
        _signal.Domains.Should().BeEmpty();
    }

    #endregion

    #region TriggerManifestGroupsAsync

    [Test]
    public async Task Group_trigger_queues_eligible_members_of_each_group_and_skips_unknown_groups()
    {
        var first = await CreateAndSaveManifestGroup(DataContext, $"a-{Guid.NewGuid():N}");
        var second = await CreateAndSaveManifestGroup(DataContext, $"b-{Guid.NewGuid():N}");
        var untouched = await CreateAndSaveManifestGroup(DataContext, $"c-{Guid.NewGuid():N}");
        var a1 = await SeedManifest(first, enabled: true);
        var a2 = await SeedManifest(first, enabled: true);
        await SeedQueuedEntry(a2, scheduledAt: null);
        var aOff = await SeedManifest(first, enabled: false);
        var aDependent = await SeedManifest(first, enabled: true, ScheduleType.Dependent, a1.Id);
        var b1 = await SeedManifest(second, enabled: true);
        var c1 = await SeedManifest(untouched, enabled: true);

        var result = await _operations.TriggerManifestGroupsAsync(
            [first.Id, second.Id, 999_999],
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Matched.Should().Be(2, "two of the ids named a group");
        result.Queued.Should().Be(2, "a1 and b1");
        result.AlreadyQueued.Should().Be(1, "a2's entry becomes the triggered run");
        result.Skipped.Should().Be(1);
        result.Notes.Should().ContainSingle(n => n.Id == 999_999);
        (await QueuedFor(aOff.Id)).Should().Be(0, "a group trigger leaves disabled members");
        (await QueuedFor(aDependent.Id)).Should().Be(0, "a dependent runs after its parent");
        (await QueuedFor(c1.Id)).Should().Be(0);
        _signal.Domains.Should().Equal([ChangeDomain.WorkQueue]);
    }

    [Test]
    public async Task Group_trigger_message_counts_groups_not_their_manifests()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        for (var i = 0; i < 3; i++)
            await SeedManifest(group, enabled: true);

        var result = await _operations.TriggerManifestGroupsAsync(
            [group.Id],
            CancellationToken.None
        );

        result.Queued.Should().Be(3);
        result.Matched.Should().Be(1);
        result.Message.Should().Be("3 queued across 1 of 1 manifest group(s).");
    }

    [Test]
    public async Task Group_trigger_counts_what_the_single_group_trigger_counts()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        await SeedManifest(group, enabled: true);
        var queued = await SeedManifest(group, enabled: true);
        await SeedQueuedEntry(queued, scheduledAt: null);

        var batch = await _operations.TriggerManifestGroupsAsync(
            [group.Id],
            CancellationToken.None
        );

        batch.Queued.Should().Be(1, "TriggerGroupAsync would report 1 for the same rows");
    }

    #endregion

    #region CancelManifestGroupsAsync

    [Test]
    public async Task Cancel_groups_flags_the_running_work_of_every_given_group()
    {
        var first = await CreateAndSaveManifestGroup(DataContext, $"a-{Guid.NewGuid():N}");
        var second = await CreateAndSaveManifestGroup(DataContext, $"b-{Guid.NewGuid():N}");
        var other = await CreateAndSaveManifestGroup(DataContext, $"c-{Guid.NewGuid():N}");
        var m1 = await SeedManifest(first, enabled: true);
        var m2 = await SeedManifest(second, enabled: true);
        var m3 = await SeedManifest(other, enabled: true);
        var pending = await SeedRun(m1.Id, TrainState.Pending);
        var running = await SeedRun(m2.Id, TrainState.InProgress);
        await SeedRun(m2.Id, TrainState.Completed);
        var elsewhere = await SeedRun(m3.Id, TrainState.InProgress);

        var result = await _operations.CancelManifestGroupsAsync(
            [first.Id, second.Id, 999_999],
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Count.Should().Be(2);
        result.Message.Should().Contain("2 of 3 manifest group(s)");
        var flagged = await DataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.CancellationRequested)
            .Select(m => m.Id)
            .ToListAsync();
        flagged.Should().BeEquivalentTo([pending.Id, running.Id]);
        flagged.Should().NotContain(elsewhere.Id);
        _signal.Domains.Should().Equal([ChangeDomain.Execution], "the change is signalled once");
    }

    [Test]
    public async Task Cancel_groups_message_counts_groups_not_their_manifests_or_runs()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        for (var i = 0; i < 3; i++)
        {
            var manifest = await SeedManifest(group, enabled: true);
            await SeedRun(manifest.Id, TrainState.InProgress);
        }

        var result = await _operations.CancelManifestGroupsAsync(
            [group.Id, 999_999],
            CancellationToken.None
        );

        result.Count.Should().Be(3);
        result
            .Message.Should()
            .Be("Cancellation requested for 3 execution(s) across 1 of 2 manifest group(s).");
    }

    [Test]
    public async Task Cancel_groups_cancels_the_runs_of_those_groups_running_on_this_host()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"a-{Guid.NewGuid():N}");
        var other = await CreateAndSaveManifestGroup(DataContext, $"b-{Guid.NewGuid():N}");
        var manifest = await SeedManifest(group, enabled: true);
        var elsewhere = await SeedManifest(other, enabled: true);
        var running = await SeedRun(manifest.Id, TrainState.InProgress);
        var finished = await SeedRun(manifest.Id, TrainState.Completed);
        var otherGroup = await SeedRun(elsewhere.Id, TrainState.InProgress);
        var registry = Scope.ServiceProvider.GetRequiredService<ICancellationRegistry>();
        using var runningToken = new CancellationTokenSource();
        using var finishedToken = new CancellationTokenSource();
        using var otherToken = new CancellationTokenSource();
        registry.Register(running.Id, runningToken);
        registry.Register(finished.Id, finishedToken);
        registry.Register(otherGroup.Id, otherToken);

        try
        {
            var result = await _operations.CancelManifestGroupsAsync(
                [group.Id],
                CancellationToken.None
            );

            result.Count.Should().Be(1, result.Message);
            runningToken.IsCancellationRequested.Should().BeTrue("it runs on this host");
            finishedToken
                .IsCancellationRequested.Should()
                .BeFalse("a finished run's token is never touched");
            otherToken.IsCancellationRequested.Should().BeFalse("its group was not named");
        }
        finally
        {
            registry.Unregister(running.Id, runningToken);
            registry.Unregister(finished.Id, finishedToken);
            registry.Unregister(otherGroup.Id, otherToken);
        }
    }

    #endregion

    #region Helpers

    private async Task<int> QueuedFor(long manifestId)
    {
        DataContext.Reset();
        return await DataContext
            .WorkQueues.AsNoTracking()
            .CountAsync(w => w.ManifestId == manifestId && w.Status == WorkQueueStatus.Queued);
    }

    private async Task<Manifest> SeedManifest(
        ManifestGroup group,
        bool enabled,
        ScheduleType scheduleType = ScheduleType.Interval,
        long? dependsOn = null
    )
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = enabled,
                ScheduleType = scheduleType,
                IntervalSeconds = scheduleType == ScheduleType.Interval ? 60 : null,
                Properties = new SchedulerTestInput { Value = "batch-trigger" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        manifest.DependsOnManifestId = dependsOn;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }

    private async Task<WorkQueue> SeedQueuedEntry(
        Manifest manifest,
        DateTime? scheduledAt,
        long? replayDecisionsOf = null
    )
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
                ScheduledAt = scheduledAt,
                ReplayDecisionsOf = replayDecisionsOf,
            }
        );
        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return entry;
    }

    private async Task<Metadata> SeedRun(long manifestId, TrainState state)
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
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return metadata;
    }

    /// <summary>
    /// Hands out the real data contexts and reports each one's tracked entities as it saves.
    /// </summary>
    private sealed class SaveObservingFactory(
        IDataContextProviderFactory inner,
        Action<DbContext> onSaving
    ) : IDataContextProviderFactory
    {
        public IEffectProvider Create() => Observe((IDataContext)inner.Create());

        public async Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken) =>
            Observe(await inner.CreateDbContextAsync(cancellationToken));

        private IDataContext Observe(IDataContext context)
        {
            ((DbContext)context).SavingChanges += (sender, _) => onSaving((DbContext)sender!);
            return context;
        }
    }

    #endregion
}
