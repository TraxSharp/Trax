using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
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
/// The batch actions the dashboard's list pages offer (cancel the selected runs or work queue
/// entries, enable or disable the selected manifests or groups) as operations-service methods,
/// so the GraphQL API can offer the same actions with the same rules instead of each surface
/// writing its own <c>ExecuteUpdate</c>.
///
/// <para>Enforces <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>:
/// the batch logic lives here, not in a surface.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[TestFixture]
public class OperationsServiceBatchTests : TestSetup
{
    private RecordingChangeSignal _signal = null!;
    private ICancellationRegistry _registry = null!;
    private OperationsService _operations = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _signal = new RecordingChangeSignal();
        _registry = Scope.ServiceProvider.GetRequiredService<ICancellationRegistry>();
        _operations = new OperationsService(
            Substitute.For<ITrainDiscoveryService>(),
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>(),
            new SchedulerConfiguration(),
            Substitute.For<ITrainExecutionService>(),
            Scope.ServiceProvider,
            changeSignal: _signal
        );
    }

    #region Validation shared by every batch

    private static IEnumerable<TestCaseData> EveryBatch()
    {
        yield return new TestCaseData(
            (Func<OperationsService, IReadOnlyCollection<long>, Task<OperationResult>>)(
                (o, ids) => o.CancelExecutionsAsync(ids, CancellationToken.None)
            )
        ).SetArgDisplayNames("CancelExecutions");
        yield return new TestCaseData(
            (Func<OperationsService, IReadOnlyCollection<long>, Task<OperationResult>>)(
                (o, ids) => o.CancelWorkQueueEntriesAsync(ids, CancellationToken.None)
            )
        ).SetArgDisplayNames("CancelWorkQueueEntries");
        yield return new TestCaseData(
            (Func<OperationsService, IReadOnlyCollection<long>, Task<OperationResult>>)(
                (o, ids) => o.SetManifestsEnabledAsync(ids, false, CancellationToken.None)
            )
        ).SetArgDisplayNames("SetManifestsEnabled");
        yield return new TestCaseData(
            (Func<OperationsService, IReadOnlyCollection<long>, Task<OperationResult>>)(
                (o, ids) => o.SetManifestGroupsEnabledAsync(ids, false, CancellationToken.None)
            )
        ).SetArgDisplayNames("SetManifestGroupsEnabled");
    }

    [TestCaseSource(nameof(EveryBatch))]
    public async Task An_empty_list_is_a_failed_result(
        Func<OperationsService, IReadOnlyCollection<long>, Task<OperationResult>> batch
    )
    {
        var result = await batch(_operations, []);

        result.Success.Should().BeFalse("an empty selection is a caller's mistake, not a no-op");
        result.Message.Should().Contain("No ids");
        _signal.Domains.Should().BeEmpty();
    }

    [TestCaseSource(nameof(EveryBatch))]
    public async Task A_list_over_the_batch_limit_is_a_failed_result(
        Func<OperationsService, IReadOnlyCollection<long>, Task<OperationResult>> batch
    )
    {
        var ids = Enumerable.Range(1, OperationsService.MaxBatchSize + 1).Select(i => (long)i);

        var result = await batch(_operations, ids.ToList());

        result.Success.Should().BeFalse();
        result.Message.Should().Contain(OperationsService.MaxBatchSize.ToString());
        _signal.Domains.Should().BeEmpty();
    }

    #endregion

    #region CancelExecutionsAsync

    [Test]
    public async Task Cancel_flags_pending_and_in_progress_runs_and_skips_the_rest()
    {
        var pending = await SeedRun(TrainState.Pending);
        var running = await SeedRun(TrainState.InProgress);
        var completed = await SeedRun(TrainState.Completed);
        var failed = await SeedRun(TrainState.Failed);

        var result = await _operations.CancelExecutionsAsync(
            [pending.Id, running.Id, completed.Id, failed.Id, 999_999],
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Count.Should().Be(2);
        (await Flagged()).Should().BeEquivalentTo([pending.Id, running.Id]);
    }

    [Test]
    public async Task Cancel_cancels_a_run_on_this_host_at_once()
    {
        var running = await SeedRun(TrainState.InProgress);
        using var cts = new CancellationTokenSource();
        _registry.Register(running.Id, cts);

        try
        {
            await _operations.CancelExecutionsAsync([running.Id], CancellationToken.None);

            cts.IsCancellationRequested.Should().BeTrue("the registry cancels a local run at once");
        }
        finally
        {
            _registry.Unregister(running.Id);
        }
    }

    [Test]
    public async Task Cancel_does_not_reach_a_terminal_run_through_the_registry()
    {
        var completed = await SeedRun(TrainState.Completed);
        using var cts = new CancellationTokenSource();
        _registry.Register(completed.Id, cts);

        try
        {
            var result = await _operations.CancelExecutionsAsync(
                [completed.Id],
                CancellationToken.None
            );

            result.Count.Should().Be(0);
            cts.IsCancellationRequested.Should().BeFalse();
        }
        finally
        {
            _registry.Unregister(completed.Id);
        }
    }

    #endregion

    #region CancelWorkQueueEntriesAsync

    [Test]
    public async Task Work_queue_cancel_touches_only_queued_entries_and_signals()
    {
        var queued = await SeedEntry(WorkQueueStatus.Queued);
        var dispatched = await SeedEntry(WorkQueueStatus.Dispatched);

        var result = await _operations.CancelWorkQueueEntriesAsync(
            [queued.Id, dispatched.Id],
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Count.Should().Be(1);
        DataContext.Reset();
        (await DataContext.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == queued.Id))
            .Status.Should()
            .Be(WorkQueueStatus.Cancelled);
        (await DataContext.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == dispatched.Id))
            .Status.Should()
            .Be(WorkQueueStatus.Dispatched);
        _signal.Domains.Should().Equal(ChangeDomain.WorkQueue);
    }

    [Test]
    public async Task Work_queue_cancel_that_changes_nothing_does_not_signal()
    {
        var dispatched = await SeedEntry(WorkQueueStatus.Dispatched);

        var result = await _operations.CancelWorkQueueEntriesAsync(
            [dispatched.Id],
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Count.Should().Be(0);
        _signal.Domains.Should().BeEmpty();
    }

    #endregion

    #region SetManifestsEnabledAsync

    [Test]
    public async Task Manifests_enable_writes_only_those_that_differ_and_signals()
    {
        var group = await CreateAndSaveManifestGroup(DataContext, $"g-{Guid.NewGuid():N}");
        var on = await SeedManifest(group, enabled: true);
        var off = await SeedManifest(group, enabled: false);

        var result = await _operations.SetManifestsEnabledAsync(
            [on.Id, off.Id],
            false,
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Count.Should().Be(1, "only the enabled one changed");
        DataContext.Reset();
        (await DataContext.Manifests.AsNoTracking().Where(m => m.IsEnabled).CountAsync())
            .Should()
            .Be(0);
        _signal.Domains.Should().Equal(ChangeDomain.Manifest);
    }

    #endregion

    #region SetManifestGroupsEnabledAsync / SetAllManifestGroupsEnabledAsync

    [Test]
    public async Task Groups_disable_writes_only_the_listed_groups_bumps_updated_at_and_signals()
    {
        var target = await CreateAndSaveManifestGroup(DataContext, $"t-{Guid.NewGuid():N}");
        var other = await CreateAndSaveManifestGroup(DataContext, $"o-{Guid.NewGuid():N}");

        var result = await _operations.SetManifestGroupsEnabledAsync(
            [target.Id],
            false,
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Count.Should().Be(1);
        DataContext.Reset();
        var fresh = await DataContext
            .ManifestGroups.AsNoTracking()
            .SingleAsync(g => g.Id == target.Id);
        fresh.IsEnabled.Should().BeFalse();
        fresh.UpdatedAt.Should().BeAfter(target.UpdatedAt);
        (await DataContext.ManifestGroups.AsNoTracking().SingleAsync(g => g.Id == other.Id))
            .IsEnabled.Should()
            .BeTrue();
        _signal.Domains.Should().Equal(ChangeDomain.ManifestGroup);
    }

    [Test]
    public async Task All_groups_disable_writes_every_group_that_differs()
    {
        await CreateAndSaveManifestGroup(DataContext, $"a-{Guid.NewGuid():N}");
        await CreateAndSaveManifestGroup(DataContext, $"b-{Guid.NewGuid():N}");
        await CreateAndSaveManifestGroup(DataContext, $"c-{Guid.NewGuid():N}", isEnabled: false);

        var result = await _operations.SetAllManifestGroupsEnabledAsync(
            false,
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        result.Count.Should().Be(2);
        DataContext.Reset();
        (await DataContext.ManifestGroups.AsNoTracking().AnyAsync(g => g.IsEnabled))
            .Should()
            .BeFalse();
        _signal.Domains.Should().Equal(ChangeDomain.ManifestGroup);
    }

    #endregion

    #region Helpers

    private async Task<List<long>> Flagged()
    {
        DataContext.Reset();
        return await DataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.CancellationRequested)
            .Select(m => m.Id)
            .ToListAsync();
    }

    private async Task<Metadata> SeedRun(TrainState state)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        metadata.TrainState = state;
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return metadata;
    }

    private async Task<WorkQueue> SeedEntry(WorkQueueStatus status)
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
        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return entry;
    }

    private async Task<Manifest> SeedManifest(ManifestGroup group, bool enabled)
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = enabled,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                Properties = new SchedulerTestInput { Value = "batch" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }

    #endregion
}
