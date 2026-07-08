using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
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
    public DeadLetterMutations DeadLetters() => new();

    /// <summary>
    /// Nested namespace exposing work queue mutations (queue a train, cancel queued entries).
    /// </summary>
    public WorkQueueMutations WorkQueue() => new();

    /// <summary>
    /// Nested namespace exposing manifest group mutations (<c>updateManifestGroup</c>).
    /// </summary>
    public ManifestGroupMutations ManifestGroups() => new();

    /// <summary>
    /// Nested namespace exposing scheduler config mutations (<c>updateScheduler</c>).
    /// </summary>
    public ConfigMutations Config() => new();

    public async Task<OperationResponse> TriggerManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.TriggerAsync(externalId, ct);
        return new OperationResponse(true, Message: "Manifest triggered");
    }

    public async Task<OperationResponse> TriggerManifestDelayed(
        string externalId,
        TimeSpan delay,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.TriggerAsync(externalId, delay, ct);
        return new OperationResponse(true, Message: $"Manifest triggered with {delay} delay");
    }

    public async Task<OperationResponse> DisableManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.DisableAsync(externalId, ct);
        return new OperationResponse(true, Message: "Manifest disabled");
    }

    public async Task<OperationResponse> EnableManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.EnableAsync(externalId, ct);
        return new OperationResponse(true, Message: "Manifest enabled");
    }

    public async Task<OperationResponse> CancelManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        var count = await scheduler.CancelAsync(externalId, ct);
        return new OperationResponse(true, Count: count, Message: "Cancellation requested");
    }

    public async Task<OperationResponse> TriggerGroup(
        long groupId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        var count = await scheduler.TriggerGroupAsync(groupId, ct);
        return new OperationResponse(true, Count: count, Message: $"{count} manifest(s) triggered");
    }

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
    /// Requests cancellation of a single execution by metadata id. Sets the durable
    /// <c>cancel_requested</c> flag on the row (only when it is still Pending or InProgress);
    /// an in-process runner observes it and transitions the train to Cancelled. Returns the
    /// number of rows flagged (0 if the execution is already terminal or missing).
    /// </summary>
    public async Task<OperationResponse> CancelExecution(
        long id,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var flagged = await db
            .Metadatas.Where(m =>
                m.Id == id
                && (m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress)
            )
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.CancellationRequested, true), ct);

        return new OperationResponse(
            flagged > 0,
            Count: flagged,
            Message: flagged > 0
                ? "Cancellation requested"
                : $"Execution {id} is not cancellable (missing or already terminal)."
        );
    }

    /// <summary>
    /// Re-queues an execution: reads its train name + input from the metadata row and enqueues
    /// a fresh work queue entry for the dispatcher, mirroring the dashboard's Re-queue action.
    /// </summary>
    public async Task<OperationResponse> RequeueExecution(
        long id,
        [Service] IDataContextProviderFactory dataContextFactory,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var meta = await db
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new { m.Name, m.Input })
            .FirstOrDefaultAsync(ct);

        if (meta is null)
            return new OperationResponse(false, Message: $"Execution {id} not found.");

        var result = await operationsService.QueueTrainAsync(
            new QueueTrainInput(meta.Name, meta.Input),
            ct
        );
        return new OperationResponse(result.Success, result.Count, result.Message);
    }

    /// <summary>
    /// Patches mutable settings on a single manifest (enabled, retries, priority, timeout,
    /// schedule). Each field on <paramref name="input"/> is independent; <c>null</c> leaves it
    /// unchanged. See <see cref="UpdateManifestInput"/> for the clear-timeout semantics.
    /// </summary>
    public async Task<OperationResponse> UpdateManifest(
        long id,
        UpdateManifestInput input,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var manifest = await db.Manifests.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (manifest is null)
            return new OperationResponse(false, Message: $"Manifest {id} not found.");

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
        return new OperationResponse(true, Count: 1, Message: "Manifest updated");
    }
}
