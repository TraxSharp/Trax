using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Validation;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Services.ChangeSignal;
using Trax.Scheduler.Services.Effects;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Scheduler management mutations: trigger, disable, enable, and cancel manifests and groups.
/// Also exposes the nested <c>deadLetters</c> namespace.
/// </summary>
public partial class OperationsMutations
{
    /// <summary>
    /// Nested namespace exposing dead letter mutations (requeue, acknowledge, batch ops).
    /// </summary>
    [NamespaceField]
    public DeadLetterMutations DeadLetters() => new();

    /// <summary>
    /// Nested namespace exposing work queue mutations (queue a train, cancel queued entries).
    /// </summary>
    [NamespaceField]
    public WorkQueueMutations WorkQueue() => new();

    /// <summary>
    /// Nested namespace exposing manifest group mutations (<c>updateManifestGroup</c>).
    /// </summary>
    [NamespaceField]
    public ManifestGroupMutations ManifestGroups() => new();

    /// <summary>
    /// Nested namespace exposing scheduler config mutations (<c>updateScheduler</c>).
    /// </summary>
    [NamespaceField]
    public ConfigMutations Config() => new();

    /// <summary>
    /// Queues an immediate run of the manifest with this external id, outside its normal schedule,
    /// which continues unaffected. A manifest holds at most one queued entry, so when it already has
    /// one, that entry is brought forward to now instead of a second one being queued. When that
    /// entry is a retry, <c>askAfresh: true</c> makes it ask its deciders again instead of replaying
    /// the failed run's decisions; if the dispatcher had already claimed the retry, it is too late to
    /// change: the mutation still succeeds, and its message says the run replays the failed run's
    /// decisions. The message says which happened (a new run queued, a queued one brought forward,
    /// or one already due released as the trigger) and <c>id</c> is that work queue entry. Through
    /// <see cref="IOperationsService.TriggerManifestAsync"/>, the call the dashboard's Trigger
    /// buttons make. An unknown external id, or a host with no database provider, where nothing
    /// dispatches the queue, returns <c>success: false</c> and changes nothing.
    /// </summary>
    public async Task<OperationResponse> TriggerManifest(
        string externalId,
        [Service] IOperationsService operationsService,
        CancellationToken ct,
        bool askAfresh = false
    ) =>
        Triggered(
            await operationsService.TriggerManifestAsync(externalId, delay: null, askAfresh, ct)
        );

    /// <summary>
    /// Queues a run of the manifest with this external id that becomes eligible for dispatch once
    /// <c>delay</c> has passed (an ISO-8601 duration such as <c>PT5M</c>). The normal schedule
    /// continues unaffected. When the manifest already has a queued entry, no second one is
    /// queued: that entry keeps its time if it is due sooner, and is brought forward to
    /// now + <c>delay</c> otherwise. With <c>askAfresh: true</c> a queued retry the trigger
    /// releases asks its deciders again instead of replaying the failed run's decisions; as with
    /// <c>triggerManifest</c>, the message says so when the dispatcher had already claimed it and it
    /// still replays. As with <c>triggerManifest</c>, the message says which happened and <c>id</c>
    /// is the entry. An unknown external id, or a host where nothing dispatches the queue, returns
    /// <c>success: false</c> and changes nothing.
    /// </summary>
    public async Task<OperationResponse> TriggerManifestDelayed(
        string externalId,
        TimeSpan delay,
        [Service] IOperationsService operationsService,
        CancellationToken ct,
        bool askAfresh = false
    ) => Triggered(await operationsService.TriggerManifestAsync(externalId, delay, askAfresh, ct));

    /// <summary>
    /// The response to a manifest trigger: the operations service's own words for what it did
    /// (queued a new run, brought a queued one forward, or released one already due), with the
    /// work queue entry's id.
    /// </summary>
    internal static OperationResponse Triggered(TriggerManifestResult result) =>
        new(result.Success, Message: result.Message) { Id = result.Trigger?.WorkQueueId };

    /// <summary>
    /// Disables the manifest with this external id so the scheduler stops running it. The manifest
    /// is kept; <c>enableManifest</c> turns it back on. An unknown external id returns
    /// <c>success: false</c> and changes nothing.
    /// </summary>
    public async Task<OperationResponse> DisableManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        if (await NotFound(externalId, dataContextFactory, ct) is { } refusal)
            return refusal;

        await scheduler.DisableAsync(externalId, ct);
        return new OperationResponse(true, Message: "Manifest disabled");
    }

    /// <summary>
    /// Enables the manifest with this external id so the scheduler runs it again. An unknown
    /// external id returns <c>success: false</c> and changes nothing.
    /// </summary>
    public async Task<OperationResponse> EnableManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        if (await NotFound(externalId, dataContextFactory, ct) is { } refusal)
            return refusal;

        await scheduler.EnableAsync(externalId, ct);
        return new OperationResponse(true, Message: "Manifest enabled");
    }

    /// <summary>
    /// Requests cancellation of every pending and running execution of the manifest with this
    /// external id. A running train stops at its next junction boundary and ends Cancelled, and is
    /// not retried. <c>count</c> is the number of executions flagged. An unknown external id
    /// returns <c>success: false</c> and changes nothing.
    /// </summary>
    public async Task<OperationResponse> CancelManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        if (await NotFound(externalId, dataContextFactory, ct) is { } refusal)
            return refusal;

        var count = await scheduler.CancelAsync(externalId, ct);
        return new OperationResponse(true, Count: count, Message: "Cancellation requested");
    }

    /// <summary>
    /// The refusal for an external id no manifest has, or <c>null</c> when one has it. The
    /// scheduler throws for an unknown id, which would reach the caller as a masked server error
    /// (Api ADR 0028). A manifest removed between this read and the scheduler's still does.
    /// </summary>
    private static async Task<OperationResponse?> NotFound(
        string externalId,
        IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);
        var exists = await db
            .Manifests.AsNoTracking()
            .AnyAsync(m => m.ExternalId == externalId, ct);
        return exists
            ? null
            : new OperationResponse(false, Message: $"Manifest '{externalId}' not found.");
    }

    /// <summary>
    /// Queues an immediate run of every enabled manifest in the group that can run on its own.
    /// Dependent manifests are skipped, since they run after their parent. <c>count</c> is the number
    /// of manifests a new run was queued for; the message also counts the ones that already had a
    /// queued run, which now runs as the trigger. Through
    /// <see cref="IOperationsService.TriggerManifestGroupsAsync"/>, the call the dashboard's group
    /// Trigger makes. A host where nothing dispatches the queue returns <c>success: false</c>.
    /// </summary>
    public async Task<OperationResponse> TriggerGroup(
        long groupId,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    )
    {
        var result = await operationsService.TriggerManifestGroupsAsync([groupId], ct);
        return new OperationResponse(result.Success, Count: result.Queued, Message: result.Message);
    }

    /// <summary>
    /// Requests cancellation of every pending and running execution of every manifest in the group.
    /// <c>count</c> is the number of executions flagged.
    /// </summary>
    public async Task<OperationResponse> CancelGroup(
        long groupId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        var count = await scheduler.CancelGroupAsync(groupId, ct);
        return new OperationResponse(
            true,
            Count: count,
            Message: $"Cancellation requested for {count} execution(s)"
        );
    }

    /// <summary>
    /// Triggers the listed manifests by id, each as <c>triggerManifest</c> triggers one: an
    /// immediate run is queued, or the manifest's queued entry is brought forward to now when it
    /// already has one. Through <see cref="IOperationsService.TriggerManifestsAsync"/>, the call the
    /// dashboard's Trigger Selected makes. With <c>askAfresh: true</c> a queued retry the trigger
    /// releases asks its deciders again instead of replaying the failed run's decisions; one the
    /// dispatcher had already claimed still replays, and is counted in <c>tooLateToAskAfresh</c>
    /// with a note. Unknown ids are skipped and noted without stopping the rest. An empty list, or
    /// more than 1000 ids, returns <c>success: false</c> and triggers nothing.
    /// </summary>
    public async Task<BatchTriggerResponse> TriggerManifests(
        long[] ids,
        [Service] IOperationsService operationsService,
        CancellationToken ct,
        bool askAfresh = false
    ) => ToResponse(await operationsService.TriggerManifestsAsync(ids, askAfresh, ct));

    /// <summary>
    /// Triggers every listed manifest group by id, each as <c>triggerGroup</c> triggers one: every
    /// enabled member that runs on its own schedule is triggered, and dependent members are left to
    /// run after their parent. Through <see cref="IOperationsService.TriggerManifestGroupsAsync"/>,
    /// the call the dashboard's Trigger Selected on the groups page makes. The counts are of
    /// manifests, except <c>matched</c> and <c>skipped</c>, which count group ids. An empty list,
    /// or more than 1000 ids, returns <c>success: false</c> and triggers nothing.
    /// </summary>
    public async Task<BatchTriggerResponse> TriggerGroups(
        long[] ids,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    ) => ToResponse(await operationsService.TriggerManifestGroupsAsync(ids, ct));

    /// <summary>
    /// Requests cancellation of every pending and running execution of every manifest in the
    /// listed groups, as <c>cancelGroup</c> does for one, through
    /// <see cref="IOperationsService.CancelManifestGroupsAsync"/>, the call the dashboard's Cancel
    /// Running makes. <c>count</c> is the number of executions flagged, zero included. An empty
    /// list, or more than 1000 ids, returns <c>success: false</c> and flags nothing.
    /// </summary>
    public async Task<OperationResponse> CancelGroups(
        long[] ids,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    ) => ToResponse(await operationsService.CancelManifestGroupsAsync(ids, ct));

    /// <summary>
    /// Requests cancellation of a single execution by id, when it is still pending or in
    /// progress. The request is durable: the process running the train sees it and ends the run
    /// as Cancelled, and a run on this host is cancelled at once. <c>count</c> is 1 when the
    /// execution was flagged; an execution that is already finished or does not exist returns
    /// <c>success: false</c> with <c>count</c> 0. So does a run a step of a user's state-machine
    /// draft started, with the reason: a user's draft is read-only to operators, and only its user
    /// cancels the run, by leaving the state (central ADR 0046).
    /// </summary>
    public async Task<OperationResponse> CancelExecution(
        long id,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    )
    {
        var result = await operationsService.CancelExecutionsAsync([id], ct);
        if (!result.Success)
            return ToResponse(result);

        return result.Count > 0
            ? new OperationResponse(true, Count: result.Count, Message: "Cancellation requested")
            : new OperationResponse(
                false,
                Count: 0,
                Message: $"Execution {id} is not cancellable (missing or already terminal)."
            );
    }

    /// <summary>
    /// Requests cancellation of the listed executions, as <c>cancelExecution</c> does for one:
    /// every one still pending or in progress is flagged, and finished or unknown ids are skipped.
    /// A run a step of a user's state-machine draft started is skipped too, and the message says
    /// how many and why. <c>count</c> is the number flagged, zero included. An empty list, or more
    /// than 1000 ids, returns <c>success: false</c> and flags nothing.
    /// </summary>
    public async Task<OperationResponse> CancelExecutions(
        long[] ids,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    ) => ToResponse(await operationsService.CancelExecutionsAsync(ids, ct));

    /// <summary>
    /// Enables or disables the listed manifests by id. Only manifests whose flag differs are
    /// written; <c>count</c> is the number changed, zero included. An empty list, or more than
    /// 1000 ids, returns <c>success: false</c> and changes nothing.
    /// </summary>
    public async Task<OperationResponse> SetManifestsEnabled(
        long[] ids,
        bool enabled,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    ) => ToResponse(await operationsService.SetManifestsEnabledAsync(ids, enabled, ct));

    /// <summary>
    /// Sets whether retries of the listed manifests replay the decisions of the run they retry
    /// (<c>replayDecisionsOnRetry</c>). Only manifests whose flag differs are written; <c>count</c>
    /// is the number changed, zero included. Turning it off also clears the replay link of each
    /// manifest's queued entry, so a retry waiting out its backoff asks afresh. An empty list, or
    /// more than 1000 ids, returns <c>success: false</c> and changes nothing.
    /// </summary>
    public async Task<OperationResponse> SetManifestsReplayDecisionsOnRetry(
        long[] ids,
        bool replay,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    ) =>
        ToResponse(
            await operationsService.SetManifestsReplayDecisionsOnRetryAsync(ids, replay, ct)
        );

    /// <summary>
    /// Re-queues an execution: queues a fresh run of the same train with the input the execution
    /// recorded, through <see cref="IOperationsService.RequeueExecutionAsync(long, bool, CancellationToken)"/>, the same call the
    /// dashboard's Re-queue button makes. Fails without queueing when the execution does not exist,
    /// recorded no input, recorded a placeholder in place of its input (too large, unserializable
    /// or disposed), or recorded an input with <c>[TraxSensitive]</c> members masked. When the
    /// execution recorded decisions, the new run replays them, so it takes the tracks the
    /// execution took; with <c>askAfresh: true</c> it asks its deciders again instead. When a
    /// queued entry or a run already replays that execution, the new run is queued afresh either
    /// way, and the message says so.
    /// </summary>
    public async Task<OperationResponse> RequeueExecution(
        long id,
        [Service] IOperationsService operationsService,
        CancellationToken ct,
        bool askAfresh = false
    ) =>
        ToResponse(
            askAfresh
                ? await operationsService.RequeueExecutionAsync(id, askAfresh: true, ct)
                : await operationsService.RequeueExecutionAsync(id, ct)
        );

    /// <summary>
    /// Resumes a failed or cancelled execution from a checkpoint instead of running every step
    /// again: queues a run of the same train, with the input the execution recorded, that skips
    /// to the step <c>from</c> names (a node id, as <c>runGraph</c> gives it), or to the step after
    /// the execution's latest checkpoint when <c>from</c> is omitted. Through
    /// <see cref="IOperationsService.ResumeExecutionAsync"/>, the same call the dashboard's Resume
    /// and Resume from here buttons make. It is a requeue in every check but where the run starts,
    /// so it enqueues through the mediator and the train's <c>[TraxAuthorize]</c> applies, and the
    /// new run replays the execution's decisions. Fails without queueing, with the reason, when
    /// the execution does not exist, is not failed or cancelled, was started by a state machine's
    /// step, has a saved input a requeue would refuse, already has a queued resume, or when no
    /// checkpoint it wrote lets it resume at that step.
    /// </summary>
    /// <param name="id">The execution (metadata) id.</param>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <param name="from">The node id of the step to resume at, or null for after the latest checkpoint.</param>
    public async Task<OperationResponse> ResumeExecution(
        long id,
        [Service] IOperationsService operationsService,
        CancellationToken ct,
        string? from = null
    ) => ToResponse(await operationsService.ResumeExecutionAsync(id, from, ct));

    /// <summary>
    /// Patches mutable settings on a single manifest (enabled, retries, priority, timeout,
    /// schedule). Each field on <paramref name="input"/> is independent; <c>null</c> leaves it
    /// unchanged. See <see cref="UpdateManifestInput"/> for the clear-timeout semantics.
    /// A value the scheduler could not use returns <c>success: false</c> with the reason and saves
    /// nothing: a timeout or interval that is not positive, a negative retry count, a priority
    /// outside 0 to 31, a cron expression the scheduler cannot parse or that never fires, a switch
    /// to <c>CRON</c> without an expression, a switch to <c>INTERVAL</c> without an interval, and a
    /// switch to <c>ONCE</c>, <c>DEPENDENT</c> or <c>DORMANT_DEPENDENT</c>, which need a time or a
    /// parent this input cannot give. The schedule is checked only when the input changes it. Through
    /// <see cref="IOperationsService.UpdateManifestAsync"/>, which runs every check.
    /// </summary>
    public async Task<OperationResponse> UpdateManifest(
        long id,
        UpdateManifestInput input,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    ) =>
        ToResponse(
            await operationsService.UpdateManifestAsync(
                id,
                new ManifestUpdate(
                    input.IsEnabled,
                    input.MaxRetries,
                    input.Priority,
                    input.TimeoutSeconds,
                    input.ClearTimeout,
                    input.ScheduleType,
                    input.CronExpression,
                    input.IntervalSeconds
                ),
                ct
            )
        );

    /// <summary>
    /// Turns the observational effect whose factory has this full type name on or off in THIS
    /// process, through <see cref="IEffectSettingsService.SetEffectEnabled"/>, the call the
    /// dashboard's effects page makes. The change is in memory: it does not reach the scheduler or
    /// worker processes where trains usually run, and a restart restores the configured state. An
    /// effect the registry does not track, or tracks as not toggleable, is refused and nothing
    /// changes, as is any effect when the host registers no effect registry. On success
    /// <c>count</c> is 1.
    /// </summary>
    public OperationResponse SetEffectEnabled(
        string fullName,
        bool enabled,
        [Service] IEffectSettingsService effectSettings
    ) => ToResponse(effectSettings.SetEffectEnabled(fullName, enabled));

    /// <summary>
    /// Writes settings of the configurable effect whose factory has this full type name, in THIS
    /// process, through <see cref="IEffectSettingsService.ConfigureEffect"/>, the call the
    /// dashboard's Configure dialog makes. Send only the settings you change: one not listed is
    /// not written. It is all or nothing: every value is read as its setting's type and checked
    /// against the setting's validation before any is written, and nothing is written when one is
    /// refused; <c>errors</c> then says why, setting by setting. A sensitive setting can be
    /// written though it is never read back. A setting listed twice, an empty list, or more than
    /// 1000 entries is refused. The change is in memory and applies to the next run in this
    /// process; it does not reach the scheduler or worker processes where trains usually run, and
    /// a restart restores the configured settings.
    /// </summary>
    /// <param name="fullName">The effect factory's full type name, as <c>effects</c> lists it.</param>
    /// <param name="values">The settings to write, by name, as text.</param>
    /// <param name="effectSettings">Resolved from DI; not a GraphQL argument.</param>
    public ConfigureEffectResponse ConfigureEffect(
        string fullName,
        IReadOnlyList<EffectSettingValueInput> values,
        [Service] IEffectSettingsService effectSettings
    )
    {
        if (values.Count > OperationsService.MaxBatchSize)
            return new ConfigureEffectResponse(
                false,
                0,
                $"At most {OperationsService.MaxBatchSize} settings can be written at once.",
                []
            );

        var repeated = values
            .GroupBy(v => v.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => new EffectSettingError(g.Key, "Given more than once."))
            .ToList();
        if (repeated.Count > 0)
            return new ConfigureEffectResponse(
                false,
                0,
                "The configuration was not saved: "
                    + string.Join(" ", repeated.Select(e => $"{e.Field}: {e.Message}")),
                repeated
            );

        var result = effectSettings.ConfigureEffect(
            fullName,
            values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal)
        );
        return new ConfigureEffectResponse(
            result.Success,
            result.Count,
            result.Message,
            result.Errors.Select(e => new EffectSettingError(e.Key, e.Value)).ToList()
        );
    }

    private static BatchTriggerResponse ToResponse(BatchTriggerResult result) =>
        new(
            result.Success,
            result.Matched,
            result.Queued,
            result.AlreadyQueued,
            result.TooLateToAskAfresh,
            result.Skipped,
            result.Message,
            result.Notes.Select(n => new BatchTriggerNote(n.Id, n.Message)).ToList()
        );

    private static OperationResponse ToResponse(OperationResult result) =>
        new(result.Success, result.Count, result.Message) { Id = result.Id };
}
