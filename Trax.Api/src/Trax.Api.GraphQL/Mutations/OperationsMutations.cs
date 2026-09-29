using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Services.ChangeSignal;
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

    /// <summary>
    /// Queues an immediate run of the manifest with this external id, outside its normal schedule,
    /// which continues unaffected. An unknown external id fails the mutation with an error.
    /// </summary>
    public async Task<OperationResponse> TriggerManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.TriggerAsync(externalId, ct);
        return new OperationResponse(true, Message: "Manifest triggered");
    }

    /// <summary>
    /// Queues a run of the manifest with this external id that becomes eligible for dispatch once
    /// <c>delay</c> has passed (an ISO-8601 duration such as <c>PT5M</c>). The normal schedule
    /// continues unaffected. An unknown external id fails the mutation with an error.
    /// </summary>
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

    /// <summary>
    /// Disables the manifest with this external id so the scheduler stops running it. The manifest
    /// is kept; <c>enableManifest</c> turns it back on. An unknown external id fails the mutation
    /// with an error.
    /// </summary>
    public async Task<OperationResponse> DisableManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.DisableAsync(externalId, ct);
        return new OperationResponse(true, Message: "Manifest disabled");
    }

    /// <summary>
    /// Enables the manifest with this external id so the scheduler runs it again. An unknown external
    /// id fails the mutation with an error.
    /// </summary>
    public async Task<OperationResponse> EnableManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.EnableAsync(externalId, ct);
        return new OperationResponse(true, Message: "Manifest enabled");
    }

    /// <summary>
    /// Requests cancellation of every pending and running execution of the manifest with this
    /// external id. A running train stops at its next junction boundary and ends Cancelled, and is
    /// not retried. <c>count</c> is the number of executions flagged. An unknown external id fails
    /// the mutation with an error.
    /// </summary>
    public async Task<OperationResponse> CancelManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        var count = await scheduler.CancelAsync(externalId, ct);
        return new OperationResponse(true, Count: count, Message: "Cancellation requested");
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
    /// as Cancelled. <c>count</c> is 1 when the execution was flagged and 0 when it is already
    /// finished or does not exist.
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
    /// Re-queues an execution: queues a fresh run of the same train with the input the execution
    /// recorded, mirroring the dashboard's Re-queue action. Fails without queueing when the
    /// execution does not exist, recorded no input, or recorded only a truncated placeholder.
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

        // An enqueue reads no input as an empty object, so re-queueing a run whose input was
        // never saved would re-run it with defaults rather than with what it ran with.
        if (string.IsNullOrWhiteSpace(meta.Input))
            return new OperationResponse(
                false,
                Message: $"Execution {id} has no saved input to re-queue it with. Inputs are "
                    + "saved only when SaveTrainParameters() is on."
            );

        // An input over MaxParameterBytes is saved as a placeholder. Re-queueing it would run
        // the train with defaults instead of with what it ran with.
        if (IsTruncatedPlaceholder(meta.Input))
            return new OperationResponse(
                false,
                Message: $"Execution {id}'s input was too large to save in full, so it cannot be "
                    + "re-queued with what it ran with."
            );

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
        [Service] ITraxChangeSignal changeSignal,
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
        changeSignal.Notify(ChangeDomain.Manifest);
        return new OperationResponse(true, Count: 1, Message: "Manifest updated");
    }

    private static bool IsTruncatedPlaceholder(string input)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(input);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.TryGetProperty("_truncated", out var truncated)
                && truncated.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
