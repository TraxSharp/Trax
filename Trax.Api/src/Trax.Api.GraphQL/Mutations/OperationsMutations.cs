using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Validation;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.EffectRegistry;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Scheduler management mutations: trigger, disable, enable, and cancel manifests and groups.
/// Also exposes the nested <c>deadLetters</c> namespace.
/// </summary>
public class OperationsMutations
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
    /// decisions. An unknown external id returns <c>success: false</c> and changes nothing.
    /// </summary>
    public async Task<OperationResponse> TriggerManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        bool askAfresh = false
    )
    {
        if (await NotFound(externalId, dataContextFactory, ct) is { } refusal)
            return refusal;

        // The overload is called only when asked, so a host whose scheduler predates it keeps the
        // default path.
        if (askAfresh)
            return Triggered(
                await scheduler.TriggerAsync(externalId, askAfresh: true, ct),
                "Manifest triggered"
            );

        await scheduler.TriggerAsync(externalId, ct);
        return new OperationResponse(
            true,
            Message: "Manifest triggered: its queued run is due now (an entry it already had is "
                + "brought forward rather than a second one queued)."
        );
    }

    /// <summary>
    /// Queues a run of the manifest with this external id that becomes eligible for dispatch once
    /// <c>delay</c> has passed (an ISO-8601 duration such as <c>PT5M</c>). The normal schedule
    /// continues unaffected. When the manifest already has a queued entry, no second one is
    /// queued: that entry keeps its time if it is due sooner, and is brought forward to
    /// now + <c>delay</c> otherwise. With <c>askAfresh: true</c> a queued retry the trigger
    /// releases asks its deciders again instead of replaying the failed run's decisions; as with
    /// <c>triggerManifest</c>, the message says so when the dispatcher had already claimed it and it
    /// still replays. An unknown external id returns <c>success: false</c> and changes nothing.
    /// </summary>
    public async Task<OperationResponse> TriggerManifestDelayed(
        string externalId,
        TimeSpan delay,
        [Service] ITraxScheduler scheduler,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        bool askAfresh = false
    )
    {
        if (await NotFound(externalId, dataContextFactory, ct) is { } refusal)
            return refusal;

        if (askAfresh)
            return Triggered(
                await scheduler.TriggerAsync(externalId, delay, askAfresh: true, ct),
                $"Manifest triggered with {delay} delay"
            );

        await scheduler.TriggerAsync(externalId, delay, ct);
        return new OperationResponse(
            true,
            Message: $"Manifest triggered: its queued run is due within {delay} (an entry it "
                + "already had keeps an earlier time or is brought forward to that one)."
        );
    }

    /// <summary>
    /// The response to a trigger asked afresh: still a success when the dispatcher had already
    /// claimed the retry, but saying that the run replays the failed run's decisions.
    /// </summary>
    internal static OperationResponse Triggered(ManifestTriggerResult result, string triggered) =>
        result.ReplayDecisionsOf is { } replayed
            ? new OperationResponse(
                true,
                Message: $"{triggered}, but the dispatcher had already claimed its queued retry, "
                    + $"so that run replays the decisions of execution {replayed} rather than "
                    + "asking its deciders afresh"
            )
            : new OperationResponse(true, Message: triggered);

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
    /// of manifests queued.
    /// </summary>
    public async Task<OperationResponse> TriggerGroup(
        long groupId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        var count = await scheduler.TriggerGroupAsync(groupId, ct);
        return new OperationResponse(true, Count: count, Message: $"{count} manifest(s) triggered");
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
    /// Requests cancellation of a single execution by id, when it is still pending or in
    /// progress. The request is durable: the process running the train sees it and ends the run
    /// as Cancelled, and a run on this host is cancelled at once. <c>count</c> is 1 when the
    /// execution was flagged; an execution that is already finished or does not exist returns
    /// <c>success: false</c> with <c>count</c> 0.
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
    /// <c>count</c> is the number flagged, zero included. An empty list, or more than 1000 ids,
    /// returns <c>success: false</c> and flags nothing.
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
    /// Patches mutable settings on a single manifest (enabled, retries, priority, timeout,
    /// schedule). Each field on <paramref name="input"/> is independent; <c>null</c> leaves it
    /// unchanged. See <see cref="UpdateManifestInput"/> for the clear-timeout semantics.
    /// A value the scheduler could not use returns <c>success: false</c> with the reason and saves
    /// nothing: a timeout or interval that is not positive, a negative retry count, a switch to
    /// <c>CRON</c> without an expression of 5 or 6 fields, a switch to <c>INTERVAL</c> without an
    /// interval, and a switch to <c>ONCE</c>, <c>DEPENDENT</c> or <c>DORMANT_DEPENDENT</c>, which
    /// need a time or a parent this input cannot give. The schedule is checked only when the input
    /// changes it.
    /// </summary>
    public async Task<OperationResponse> UpdateManifest(
        long id,
        UpdateManifestInput input,
        [Service] IDataContextProviderFactory dataContextFactory,
        [Service] ITraxChangeSignal changeSignal,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var manifest = await db.Manifests.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (manifest is null)
            return new OperationResponse(false, Message: $"Manifest {id} not found.");

        if (
            UpdateManifestRefusal(
                manifest.ScheduleType,
                manifest.CronExpression,
                manifest.IntervalSeconds,
                input
            ) is
            { } refusal
        )
            return new OperationResponse(
                false,
                Message: $"Manifest {id} was not updated: {refusal}"
            );

        if (input.IsEnabled.HasValue)
            manifest.IsEnabled = input.IsEnabled.Value;
        if (input.MaxRetries.HasValue)
            manifest.MaxRetries = input.MaxRetries.Value;
        if (input.Priority.HasValue)
            manifest.Priority = input.Priority.Value;
        if (input.ClearTimeout)
            manifest.TimeoutSeconds = null;
        else if (input.TimeoutSeconds.HasValue)
            manifest.TimeoutSeconds = input.TimeoutSeconds.Value;
        if (input.ScheduleType.HasValue)
            manifest.ScheduleType = input.ScheduleType.Value;
        if (input.CronExpression is not null)
            manifest.CronExpression = input.CronExpression;
        if (input.IntervalSeconds.HasValue)
            manifest.IntervalSeconds = input.IntervalSeconds.Value;

        await db.SaveChanges(ct);
        changeSignal.Notify(ChangeDomain.Manifest);
        return new OperationResponse(true, Count: 1, Message: "Manifest updated");
    }

    /// <summary>
    /// Why the scheduler could not use the manifest <paramref name="input"/> would leave, or
    /// <c>null</c> when it could. The scheduler skips a manifest whose schedule it cannot evaluate
    /// on every poll, and turns a zero timeout into one that cancels every run at once.
    /// </summary>
    /// <remarks>
    /// The cron check is the scheduler's own first step, a count of 5 or 6 fields; an expression
    /// with the right count and a bad field is caught by the scheduler, not here, because the
    /// parser it uses is internal to <c>Trax.Scheduler</c>.
    /// </remarks>
    private static string? UpdateManifestRefusal(
        ScheduleType currentType,
        string? currentCron,
        int? currentInterval,
        UpdateManifestInput input
    )
    {
        if (input.MaxRetries is < 0)
            return "maxRetries may not be negative.";
        if (!input.ClearTimeout && input.TimeoutSeconds is <= 0)
            return "timeoutSeconds must be greater than 0; set clearTimeout to remove the timeout.";
        if (input.IntervalSeconds is <= 0)
            return "intervalSeconds must be greater than 0.";

        var changesSchedule =
            input.ScheduleType.HasValue
            || input.CronExpression is not null
            || input.IntervalSeconds.HasValue;
        if (!changesSchedule)
            return null;

        var type = input.ScheduleType ?? currentType;
        if (
            type is ScheduleType.Once or ScheduleType.Dependent or ScheduleType.DormantDependent
            && type != currentType
        )
            return $"a manifest cannot be switched to {type} here: it needs a "
                + (type == ScheduleType.Once ? "time" : "parent manifest")
                + " this input cannot give. Schedule it from code instead.";

        var cron = input.CronExpression ?? currentCron;
        if (type == ScheduleType.Cron || input.CronExpression is not null)
        {
            if (string.IsNullOrWhiteSpace(cron))
                return "a CRON schedule needs a cronExpression.";
            var fields = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (fields is not (5 or 6))
                return $"cronExpression '{cron}' has {fields} field(s); a cron expression has 5 "
                    + "or 6 (with seconds).";
        }

        if (type == ScheduleType.Interval && (input.IntervalSeconds ?? currentInterval) is null)
            return "an INTERVAL schedule needs intervalSeconds.";

        return null;
    }

    /// <summary>
    /// Turns the observational effect whose factory has this full type name on or off in THIS
    /// process, through the effect registry exactly as the dashboard's effects page does. The
    /// change is in memory: it does not reach the scheduler or worker processes where trains
    /// usually run, and a restart restores the configured state. An effect the registry does not
    /// track, or tracks as not toggleable, is refused and nothing changes. On success
    /// <c>count</c> is 1.
    /// </summary>
    public OperationResponse SetEffectEnabled(
        string fullName,
        bool enabled,
        [Service] IEffectRegistry registry
    )
    {
        var factoryType = registry
            .GetAll()
            .Keys.FirstOrDefault(t =>
                string.Equals(t.FullName ?? t.Name, fullName, StringComparison.Ordinal)
            );

        if (factoryType is null)
            return new OperationResponse(
                false,
                Message: $"No effect named '{fullName}' is registered in this process."
            );

        if (!registry.IsToggleable(factoryType))
            return new OperationResponse(
                false,
                Message: $"The effect '{fullName}' is registered as not toggleable."
            );

        if (enabled)
            registry.Enable(factoryType);
        else
            registry.Disable(factoryType);

        return new OperationResponse(
            true,
            Count: 1,
            Message: enabled ? "Effect enabled in this process" : "Effect disabled in this process"
        );
    }

    private static OperationResponse ToResponse(OperationResult result) =>
        new(result.Success, result.Count, result.Message) { Id = result.Id };
}
