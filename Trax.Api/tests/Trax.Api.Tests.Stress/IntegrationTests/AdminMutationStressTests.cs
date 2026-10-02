using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.Tests.Stress.Fakes.Trains;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.EffectRegistry;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// One SLA test per single-row or scoped administrative mutation, each run against the same
/// millions-of-rows seed as <see cref="AdminEndpointStressTests"/>. A mutation's cost at scale is
/// the lookup that finds its rows and the index maintenance its write triggers, both of which
/// grow with the tables it touches.
/// </summary>
/// <remarks>
/// Every test acts on rows chosen by the seed's arithmetic (see <c>BulkSeeder</c>) and puts them
/// back afterwards, so the read suite still sees the seed and the suite can be re-run without
/// reseeding. Batch dead-letter writes, whose scope is the whole table, are in
/// <see cref="DeadLetterBatchStressTests"/>.
/// </remarks>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class AdminMutationStressTests : StressTestSetup
{
    /// <summary>A mid-table manifest, enabled and scheduled by the seed (id % 5 == 1: interval).</summary>
    private const long ManifestId = 2_501;
    private const string ManifestExternalId = "stress-manifest-2501";

    /// <summary>A mid-table group; the seed puts 1 in every Groups manifests in it.</summary>
    private const long GroupId = 101;

    /// <summary>
    /// The seed gives dead letter <c>g</c> the status <c>[awaiting, awaiting, retried,
    /// acknowledged][g % 4]</c> and manifest <c>1 + g % Manifests</c>, so an id divisible by four
    /// is awaiting intervention.
    /// </summary>
    private const long DeadLetterId = 400_000;

    /// <summary>The seed gives work queue row <c>g</c> the status queued when <c>g % 4 == 3</c>.</summary>
    private const long QueuedWorkQueueId = 750_003;

    private static readonly string ProbeTrainName = typeof(IStressProbeTrain).FullName!;

    private static IDataContextProviderFactory Factory(IServiceProvider sp) =>
        sp.GetRequiredService<IDataContextProviderFactory>();

    private static ITraxScheduler Scheduler(IServiceProvider sp) =>
        sp.GetRequiredService<ITraxScheduler>();

    private static IOperationsService Operations(IServiceProvider sp) =>
        sp.GetRequiredService<IOperationsService>();

    /// <summary>
    /// A page of dead letters awaiting intervention, as an operator selects them, in pairs that
    /// share a manifest: <c>g</c> and <c>g + 4 * Manifests</c> have the same manifest and the same
    /// seeded status. A requeue folds each pair into one work queue entry and resolves both.
    /// </summary>
    private static long[] AwaitingDeadLetterIds(int count) =>
        Enumerable
            .Range(0, (count + 1) / 2)
            .SelectMany(k =>
                new[] { DeadLetterId + 4L * k, DeadLetterId + 4L * k + 4L * Profile.Manifests }
            )
            .Take(count)
            .ToArray();

    private static Task RestoreDeadLetters(long[] ids) =>
        ExecSqlAsync(
            $"DELETE FROM trax.work_queue WHERE dead_letter_id IN ({string.Join(',', ids)}); "
                + "UPDATE trax.dead_letter SET status = 'awaiting_intervention', resolved_at = NULL, "
                + $"resolution_note = NULL, retry_metadata_id = NULL WHERE id IN ({string.Join(',', ids)})"
        );

    private static Task DeleteQueuedEntriesForManifestsOf(string manifestFilter) =>
        ExecSqlAsync(
            "DELETE FROM trax.work_queue WHERE status = 'queued' AND manifest_id IN "
                + $"(SELECT id FROM trax.manifest WHERE {manifestFilter})"
        );

    #region Per-process writes

    [Test]
    public async Task SetEffectEnabled_WithinBudget()
    {
        // Per-process and in memory, so its cost does not grow with the seed; the budget pins
        // that it stays a registry write and never picks up a database round trip.
        await MeasureAsync(
            "operations.setEffectEnabled",
            TrivialBudget,
            (sp, _) =>
            {
                var effects = sp.GetRequiredService<IEffectRegistry>();
                var toggleable = effects.GetToggleable().Keys.First();
                var mutations = new OperationsMutations();
                mutations
                    .SetEffectEnabled(toggleable.FullName!, false, effects)
                    .Success.Should()
                    .BeTrue();
                mutations
                    .SetEffectEnabled(toggleable.FullName!, true, effects)
                    .Success.Should()
                    .BeTrue();
                return Task.CompletedTask;
            }
        );
    }

    #endregion

    #region Manifest writes

    [Test]
    public async Task TriggerManifest_AtScale_WithinBudget()
    {
        await MeasureWriteAsync(
            "operations.triggerManifest",
            ListBudget,
            () => DeleteQueuedEntriesForManifestsOf($"id = {ManifestId}"),
            async (sp, ct) =>
                (
                    await new OperationsMutations().TriggerManifest(
                        ManifestExternalId,
                        Scheduler(sp),
                        ct
                    )
                )
                    .Success.Should()
                    .BeTrue()
        );
    }

    [Test]
    public async Task TriggerManifestDelayed_AtScale_WithinBudget()
    {
        await MeasureWriteAsync(
            "operations.triggerManifestDelayed",
            ListBudget,
            () => DeleteQueuedEntriesForManifestsOf($"id = {ManifestId}"),
            async (sp, ct) =>
                (
                    await new OperationsMutations().TriggerManifestDelayed(
                        ManifestExternalId,
                        TimeSpan.FromMinutes(5),
                        Scheduler(sp),
                        ct
                    )
                )
                    .Success.Should()
                    .BeTrue()
        );
    }

    [Test]
    public async Task DisableManifest_AtScale_WithinBudget()
    {
        await MeasureWriteAsync(
            "operations.disableManifest",
            ListBudget,
            () =>
                ExecSqlAsync($"UPDATE trax.manifest SET is_enabled = true WHERE id = {ManifestId}"),
            async (sp, ct) =>
                await new OperationsMutations().DisableManifest(
                    ManifestExternalId,
                    Scheduler(sp),
                    ct
                )
        );
    }

    [Test]
    public async Task EnableManifest_AtScale_WithinBudget()
    {
        await MeasureWriteAsync(
            "operations.enableManifest",
            ListBudget,
            () =>
                ExecSqlAsync(
                    $"UPDATE trax.manifest SET is_enabled = false WHERE id = {ManifestId}"
                ),
            async (sp, ct) =>
                await new OperationsMutations().EnableManifest(
                    ManifestExternalId,
                    Scheduler(sp),
                    ct
                ),
            restore: () =>
                ExecSqlAsync($"UPDATE trax.manifest SET is_enabled = true WHERE id = {ManifestId}")
        );
    }

    [Test]
    public async Task CancelManifest_AtScale_WithinBudget()
    {
        // The manifest's in-progress runs are found through the metadata table's manifest_id,
        // so this is a filter over millions of rows, not a point write.
        await MeasureWriteAsync(
            "operations.cancelManifest",
            ListBudget,
            () =>
                ExecSqlAsync(
                    "UPDATE trax.metadata SET cancel_requested = false "
                        + $"WHERE manifest_id = {ManifestId} AND cancel_requested"
                ),
            async (sp, ct) =>
                (
                    await new OperationsMutations().CancelManifest(
                        ManifestExternalId,
                        Scheduler(sp),
                        ct
                    )
                )
                    .Count.Should()
                    .BeGreaterThan(0)
        );
    }

    [Test]
    public async Task UpdateManifest_AtScale_WithinBudget()
    {
        var original = await ScalarAsync<int>(
            $"SELECT priority FROM trax.manifest WHERE id = {ManifestId}"
        );

        await MeasureWriteAsync(
            "operations.updateManifest",
            ListBudget,
            () =>
                ExecSqlAsync(
                    $"UPDATE trax.manifest SET priority = {original} WHERE id = {ManifestId}"
                ),
            async (sp, ct) =>
                (
                    await new OperationsMutations().UpdateManifest(
                        ManifestId,
                        new UpdateManifestInput(Priority: original + 7, MaxRetries: 4),
                        Factory(sp),
                        sp.GetRequiredService<ITraxChangeSignal>(),
                        ct
                    )
                )
                    .Success.Should()
                    .BeTrue()
        );
    }

    #endregion

    #region Group writes

    [Test]
    public async Task TriggerGroup_AtScale_WithinBudget()
    {
        await MeasureWriteAsync(
            "operations.triggerGroup",
            ListBudget,
            () => DeleteQueuedEntriesForManifestsOf($"manifest_group_id = {GroupId}"),
            async (sp, ct) =>
                (await new OperationsMutations().TriggerGroup(GroupId, Scheduler(sp), ct))
                    .Count.Should()
                    .BeGreaterThan(0)
        );
    }

    [Test]
    public async Task CancelGroup_AtScale_WithinBudget()
    {
        // Joins metadata to the group's manifests: the widest of the cancel paths.
        await MeasureWriteAsync(
            "operations.cancelGroup",
            ListBudget,
            () =>
                ExecSqlAsync(
                    "UPDATE trax.metadata SET cancel_requested = false WHERE cancel_requested "
                        + "AND manifest_id IN (SELECT id FROM trax.manifest "
                        + $"WHERE manifest_group_id = {GroupId})"
                ),
            async (sp, ct) =>
                (await new OperationsMutations().CancelGroup(GroupId, Scheduler(sp), ct))
                    .Count.Should()
                    .BeGreaterThan(0)
        );
    }

    [Test]
    public async Task UpdateManifestGroup_AtScale_WithinBudget()
    {
        var original = await ScalarAsync<int>(
            $"SELECT priority FROM trax.manifest_group WHERE id = {GroupId}"
        );

        await MeasureWriteAsync(
            "operations.manifestGroups.updateManifestGroup",
            ListBudget,
            () =>
                ExecSqlAsync(
                    $"UPDATE trax.manifest_group SET priority = {original} WHERE id = {GroupId}"
                ),
            async (sp, ct) =>
                (
                    await new ManifestGroupMutations().UpdateManifestGroup(
                        GroupId,
                        new UpdateManifestGroupInput(Priority: original + 5),
                        Operations(sp),
                        ct
                    )
                )
                    .Success.Should()
                    .BeTrue()
        );
    }

    #endregion

    #region Execution and work queue writes

    [Test]
    public async Task RequeueExecution_AtScale_WithinBudget()
    {
        // A seeded run has no saved input and names a train nothing registers, so one run is
        // pointed at the probe train with an input for the duration of the test.
        var executionId = Profile.Metadata / 3;
        var seededName = await ScalarAsync<string>(
            $"SELECT name FROM trax.metadata WHERE id = {executionId}"
        );

        await MeasureWriteAsync(
            "operations.requeueExecution",
            ListBudget,
            () =>
                ExecSqlAsync(
                    "DELETE FROM trax.work_queue WHERE status = 'queued' "
                        + $"AND train_name = '{ProbeTrainName}'; "
                        + $"UPDATE trax.metadata SET name = '{ProbeTrainName}', "
                        + $"input = '{{\"Value\":\"stress\"}}' WHERE id = {executionId}"
                ),
            async (sp, ct) =>
            {
                var response = await new OperationsMutations().RequeueExecution(
                    executionId,
                    Operations(sp),
                    ct
                );
                response.Success.Should().BeTrue(response.Message);
            },
            restore: () =>
                ExecSqlAsync(
                    "DELETE FROM trax.work_queue WHERE status = 'queued' "
                        + $"AND train_name = '{ProbeTrainName}'; "
                        + $"UPDATE trax.metadata SET name = '{seededName}', input = NULL "
                        + $"WHERE id = {executionId}"
                )
        );
    }

    [Test]
    public async Task QueueTrain_AtScale_WithinBudget()
    {
        await MeasureWriteAsync(
            "operations.workQueue.queueTrain",
            ListBudget,
            () =>
                ExecSqlAsync(
                    "DELETE FROM trax.work_queue WHERE status = 'queued' "
                        + $"AND train_name = '{ProbeTrainName}'"
                ),
            async (sp, ct) =>
            {
                var response = await new WorkQueueMutations().QueueTrain(
                    new QueueTrainInput(ProbeTrainName, "{\"Value\":\"stress\"}"),
                    Operations(sp),
                    ct
                );
                response.Success.Should().BeTrue(response.Message);
            }
        );
    }

    [Test]
    public async Task RunTrain_AtScale_WithinBudget()
    {
        // A run writes its Pending metadata row, then hands it to the default submitter, which
        // writes a background job. Both inserts land on tables of millions of rows. The seed
        // names no probe-train runs, so removing those afterwards leaves the seed as it was.
        Task RemoveProbeRuns() =>
            ExecSqlAsync(
                "DELETE FROM trax.background_job WHERE metadata_id IN "
                    + $"(SELECT id FROM trax.metadata WHERE name = '{ProbeTrainName}'); "
                    + $"DELETE FROM trax.metadata WHERE name = '{ProbeTrainName}'"
            );

        await MeasureWriteAsync(
            "operations.workQueue.runTrain",
            ListBudget,
            RemoveProbeRuns,
            async (sp, ct) =>
            {
                var response = await new WorkQueueMutations().RunTrain(
                    new RunTrainInput(ProbeTrainName, "{\"Value\":\"stress\"}"),
                    Operations(sp),
                    ct
                );
                response.Success.Should().BeTrue(response.Message);
                response.Id.Should().BeGreaterThan(Profile.Metadata, "the id is the new run's");
            }
        );
    }

    [Test]
    public async Task CancelWorkQueueEntry_AtScale_WithinBudget()
    {
        await MeasureWriteAsync(
            "operations.workQueue.cancelWorkQueueEntry",
            ListBudget,
            () =>
                ExecSqlAsync(
                    $"UPDATE trax.work_queue SET status = 'queued' WHERE id = {QueuedWorkQueueId}"
                ),
            async (sp, ct) =>
            {
                var response = await new WorkQueueMutations().CancelWorkQueueEntry(
                    QueuedWorkQueueId,
                    Operations(sp),
                    ct
                );
                response.Success.Should().BeTrue(response.Message);
            }
        );
    }

    #endregion

    #region Batch writes (one dashboard selection of at most OperationsService.MaxBatchSize ids)

    /// <summary>A full batch of consecutive ids starting at <paramref name="first"/>.</summary>
    private static long[] Batch(long first) =>
        Enumerable.Range(0, OperationsService.MaxBatchSize).Select(i => first + i).ToArray();

    private static string InList(long[] ids) => string.Join(',', ids);

    [Test]
    public async Task CancelExecutions_FullBatch_WithinBudget()
    {
        // The seed's state mix puts a pending or running run at every g % 9 of 6 and 7, so a
        // mid-table block of a thousand ids flags about two hundred of them.
        var ids = Batch(Profile.Metadata / 2);
        await MeasureWriteAsync(
            "operations.cancelExecutions (1000 ids)",
            ListBudget,
            () =>
                ExecSqlAsync(
                    $"UPDATE trax.metadata SET cancel_requested = false WHERE id IN ({InList(ids)})"
                ),
            async (sp, ct) =>
            {
                var response = await new OperationsMutations().CancelExecutions(
                    ids,
                    Operations(sp),
                    ct
                );
                response.Success.Should().BeTrue(response.Message);
                response.Count.Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task CancelWorkQueueEntries_FullBatch_WithinBudget()
    {
        // The seed queues every work queue row with g % 4 == 3; restoring sets those back.
        var ids = Batch(Profile.WorkQueue / 2);
        await MeasureWriteAsync(
            "operations.workQueue.cancelWorkQueueEntries (1000 ids)",
            ListBudget,
            () =>
                ExecSqlAsync(
                    $"UPDATE trax.work_queue SET status = 'queued' WHERE id IN ({InList(ids)}) "
                        + "AND id % 4 = 3"
                ),
            async (sp, ct) =>
            {
                var response = await new WorkQueueMutations().CancelWorkQueueEntries(
                    ids,
                    Operations(sp),
                    ct
                );
                response.Success.Should().BeTrue(response.Message);
                response.Count.Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task SetManifestsEnabled_FullBatch_WithinBudget()
    {
        var ids = Batch(Profile.Manifests / 2 - OperationsService.MaxBatchSize / 2);
        await MeasureWriteAsync(
            "operations.setManifestsEnabled (1000 ids)",
            ListBudget,
            () =>
                ExecSqlAsync(
                    $"UPDATE trax.manifest SET is_enabled = true WHERE id IN ({InList(ids)})"
                ),
            async (sp, ct) =>
            {
                var response = await new OperationsMutations().SetManifestsEnabled(
                    ids,
                    false,
                    Operations(sp),
                    ct
                );
                response.Count.Should().Be(ids.Length, response.Message);
            }
        );
    }

    [Test]
    public async Task SetManifestGroupsEnabled_EveryGroup_WithinBudget()
    {
        var ids = Enumerable
            .Range(1, Math.Min(Profile.Groups, OperationsService.MaxBatchSize))
            .Select(i => (long)i)
            .ToArray();
        await MeasureWriteAsync(
            "operations.manifestGroups.setManifestGroupsEnabled",
            ListBudget,
            () =>
                ExecSqlAsync(
                    $"UPDATE trax.manifest_group SET is_enabled = true WHERE id IN ({InList(ids)})"
                ),
            async (sp, ct) =>
            {
                var response = await new ManifestGroupMutations().SetManifestGroupsEnabled(
                    ids,
                    false,
                    Operations(sp),
                    ct
                );
                response.Count.Should().Be(ids.Length, response.Message);
            }
        );
    }

    [Test]
    public async Task SetAllManifestGroupsEnabled_WithinBudget()
    {
        await MeasureWriteAsync(
            "operations.manifestGroups.setAllManifestGroupsEnabled",
            ListBudget,
            () => ExecSqlAsync("UPDATE trax.manifest_group SET is_enabled = true"),
            async (sp, ct) =>
            {
                var response = await new ManifestGroupMutations().SetAllManifestGroupsEnabled(
                    false,
                    Operations(sp),
                    ct
                );
                response.Count.Should().Be(Profile.Groups, response.Message);
            }
        );
    }

    #endregion

    #region Dead letter writes (one row, or one dashboard selection)

    [Test]
    public async Task RequeueDeadLetter_AtScale_WithinBudget()
    {
        long[] ids = [DeadLetterId];
        await MeasureWriteAsync(
            "operations.deadLetters.requeueDeadLetter",
            ListBudget,
            () => RestoreDeadLetters(ids),
            async (sp, ct) =>
                (await new DeadLetterMutations().RequeueDeadLetter(DeadLetterId, Scheduler(sp), ct))
                    .Success.Should()
                    .BeTrue()
        );
    }

    [Test]
    public async Task AcknowledgeDeadLetter_AtScale_WithinBudget()
    {
        long[] ids = [DeadLetterId];
        await MeasureWriteAsync(
            "operations.deadLetters.acknowledgeDeadLetter",
            ListBudget,
            () => RestoreDeadLetters(ids),
            async (sp, ct) =>
                (
                    await new DeadLetterMutations().AcknowledgeDeadLetter(
                        DeadLetterId,
                        "stress",
                        Scheduler(sp),
                        ct
                    )
                )
                    .Success.Should()
                    .BeTrue()
        );
    }

    [Test]
    public async Task RequeueDeadLetters_Selection_WithinBudget()
    {
        // One dashboard page selected and requeued together.
        var ids = AwaitingDeadLetterIds(25);
        await MeasureWriteAsync(
            "operations.deadLetters.requeueDeadLetters (25)",
            ListBudget,
            () => RestoreDeadLetters(ids),
            async (sp, ct) =>
                (await new DeadLetterMutations().RequeueDeadLetters(ids, Scheduler(sp), ct))
                    .Count.Should()
                    .Be(ids.Length)
        );
    }

    [Test]
    public async Task AcknowledgeDeadLetters_Selection_WithinBudget()
    {
        var ids = AwaitingDeadLetterIds(25);
        await MeasureWriteAsync(
            "operations.deadLetters.acknowledgeDeadLetters (25)",
            ListBudget,
            () => RestoreDeadLetters(ids),
            async (sp, ct) =>
                (
                    await new DeadLetterMutations().AcknowledgeDeadLetters(
                        ids,
                        "stress",
                        Scheduler(sp),
                        ct
                    )
                )
                    .Count.Should()
                    .Be(ids.Length)
        );
    }

    #endregion

    #region Configuration

    [Test]
    public async Task UpdateScheduler_AtScale_WithinBudget()
    {
        var original = Operations(Services).GetSchedulerConfig().DefaultMaxRetries;

        await MeasureWriteAsync(
            "operations.config.updateScheduler",
            ListBudget,
            async () =>
            {
                using var scope = Services.CreateScope();
                await Operations(scope.ServiceProvider)
                    .UpdateSchedulerConfigAsync(
                        new UpdateSchedulerConfigInput(DefaultMaxRetries: original),
                        CancellationToken.None
                    );
            },
            async (sp, ct) =>
                (
                    await new ConfigMutations().UpdateScheduler(
                        new UpdateSchedulerConfigInput(DefaultMaxRetries: original + 1),
                        Operations(sp),
                        ct
                    )
                )
                    .Success.Should()
                    .BeTrue()
        );
    }

    #endregion
}
