using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Services.ChangeSignal;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.GraphQL.Mutations;

public partial class OperationsMutations
{
    /// <summary>
    /// The resolver as it shipped before it went through <see cref="IOperationsService"/>, kept
    /// so an assembly built against it still binds. It is not a GraphQL field: the field is the
    /// overload taking <see cref="IOperationsService"/>, which the dashboard shares.
    /// </summary>
    [GraphQLIgnore]
    [EditorBrowsable(EditorBrowsableState.Never)]
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
    /// The resolver as it shipped before it went through <see cref="IOperationsService"/>, kept
    /// so an assembly built against it still binds. It is not a GraphQL field: the field is the
    /// overload taking <see cref="IOperationsService"/>, which the dashboard shares.
    /// </summary>
    [GraphQLIgnore]
    [EditorBrowsable(EditorBrowsableState.Never)]
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
    /// The resolver as it shipped before it went through <see cref="IOperationsService"/>, kept
    /// so an assembly built against it still binds. It is not a GraphQL field: the field is the
    /// overload taking <see cref="IOperationsService"/>, which the dashboard shares.
    /// </summary>
    [GraphQLIgnore]
    [EditorBrowsable(EditorBrowsableState.Never)]
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
    /// The resolver as it shipped before it went through <see cref="IOperationsService"/>, kept
    /// so an assembly built against it still binds. It is not a GraphQL field: the field is the
    /// overload taking <see cref="IOperationsService"/>, which the dashboard shares.
    /// </summary>
    [GraphQLIgnore]
    [EditorBrowsable(EditorBrowsableState.Never)]
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
    /// The response to a trigger asked afresh: still a success when the dispatcher had already
    /// claimed the retry, but saying that the run replays the failed run's decisions.
    /// </summary>
    private static OperationResponse Triggered(ManifestTriggerResult result, string triggered) =>
        result.ReplayDecisionsOf is { } replayed
            ? new OperationResponse(
                true,
                Message: $"{triggered}, but the dispatcher had already claimed its queued retry, "
                    + $"so that run replays the decisions of execution {replayed} rather than "
                    + "asking its deciders afresh"
            )
            : new OperationResponse(true, Message: triggered);
}
