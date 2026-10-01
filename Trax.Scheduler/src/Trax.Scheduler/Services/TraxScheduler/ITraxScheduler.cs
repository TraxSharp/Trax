using LanguageExt;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Services.TraxScheduler;

/// <summary>
/// Provides a type-safe API for scheduling trains as recurring jobs.
/// </summary>
public interface ITraxScheduler
{
    /// <summary>
    /// Schedules a single train to run on a recurring basis.
    /// </summary>
    /// <typeparam name="TTrain">
    /// The train interface type. Must implement IServiceTrain&lt;TInput, TOutput&gt;
    /// for some TOutput. The scheduler resolves the train via TrainBus using the input type.
    /// </typeparam>
    /// <typeparam name="TInput">
    /// The input type for the train. Must implement IManifestProperties to enable
    /// serialization for scheduled job storage.
    /// </typeparam>
    /// <typeparam name="TOutput">The output type of <typeparamref name="TTrain"/>, from its <c>IServiceTrain&lt;TInput, TOutput&gt;</c> interface.</typeparam>
    /// <param name="externalId">
    /// A unique identifier for this scheduled job. Used for upsert semantics -
    /// if a manifest with this ID exists, it will be updated; otherwise, a new one is created.
    /// </param>
    /// <param name="input">The input data that will be passed to the train on each execution.</param>
    /// <param name="schedule">The schedule definition (interval or cron-based).</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created or updated manifest.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the train is not registered in the TrainRegistry.
    /// </exception>
    Task<Manifest> ScheduleAsync<TTrain, TInput, TOutput>(
        string externalId,
        TInput input,
        Schedule schedule,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties;

    /// <summary>
    /// Schedules multiple instances of a train from a collection.
    /// </summary>
    /// <typeparam name="TTrain">
    /// The train interface type. Must implement IServiceTrain&lt;TInput, TOutput&gt;
    /// for some TOutput. The scheduler resolves the train via TrainBus using the input type.
    /// </typeparam>
    /// <typeparam name="TInput">
    /// The input type for the train. Must implement IManifestProperties to enable
    /// serialization for scheduled job storage.
    /// </typeparam>
    /// <typeparam name="TOutput">The output type of <typeparamref name="TTrain"/>, from its <c>IServiceTrain&lt;TInput, TOutput&gt;</c> interface.</typeparam>
    /// <typeparam name="TSource">The type of elements in the source collection.</typeparam>
    /// <param name="sources">The collection of source items to create manifests from.</param>
    /// <param name="map">
    /// A function that transforms each source item into an ExternalId and Input pair.
    /// </param>
    /// <param name="schedule">The schedule definition applied to all manifests.</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/>.</param>
    /// <param name="configureEach">
    /// Optional action to configure per-item manifest options. Invoked for each source item
    /// after the base options from <paramref name="options"/> are applied, allowing per-item overrides.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A read-only list of the created or updated manifests.
    /// </returns>
    /// <remarks>
    /// All manifests are created/updated in a single transaction. If any manifest
    /// fails to save, the entire batch is rolled back. Pruning (if enabled via
    /// <see cref="ScheduleOptions.PrunePrefix"/>) is also included in the same transaction.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the train is not registered in the TrainRegistry.
    /// </exception>
    Task<IReadOnlyList<Manifest>> ScheduleManyAsync<TTrain, TInput, TOutput, TSource>(
        IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, TInput Input)> map,
        Schedule schedule,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties;

    /// <summary>
    /// Schedules a single train that depends on another manifest's successful completion.
    /// </summary>
    /// <typeparam name="TTrain">The train interface type.</typeparam>
    /// <typeparam name="TInput">The input type for the train.</typeparam>
    /// <typeparam name="TOutput">The output type of <typeparamref name="TTrain"/>, from its <c>IServiceTrain&lt;TInput, TOutput&gt;</c> interface.</typeparam>
    /// <param name="externalId">A unique identifier for this dependent job.</param>
    /// <param name="input">The input data that will be passed to the train on each execution.</param>
    /// <param name="dependsOnExternalId">The external ID of the parent manifest this job depends on.</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created or updated manifest.</returns>
    Task<Manifest> ScheduleDependentAsync<TTrain, TInput, TOutput>(
        string externalId,
        TInput input,
        string dependsOnExternalId,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties;

    /// <summary>
    /// Schedules multiple dependent train instances from a collection.
    /// </summary>
    /// <typeparam name="TTrain">The train interface type.</typeparam>
    /// <typeparam name="TInput">The input type for the train.</typeparam>
    /// <typeparam name="TOutput">The output type of <typeparamref name="TTrain"/>, from its <c>IServiceTrain&lt;TInput, TOutput&gt;</c> interface.</typeparam>
    /// <typeparam name="TSource">The type of elements in the source collection.</typeparam>
    /// <param name="sources">The collection of source items to create manifests from.</param>
    /// <param name="map">A function that transforms each source item into an ExternalId and Input pair.</param>
    /// <param name="dependsOn">A function that maps each source item to the external ID of its parent manifest.</param>
    /// <param name="options">Optional callback to configure manifest and group options via <see cref="ScheduleOptions"/>.</param>
    /// <param name="configureEach">Optional action to configure per-item manifest options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of the created or updated manifests.</returns>
    Task<IReadOnlyList<Manifest>> ScheduleManyDependentAsync<TTrain, TInput, TOutput, TSource>(
        IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, TInput Input)> map,
        Func<TSource, string> dependsOn,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties;

    /// <summary>
    /// Disables a scheduled job, preventing future executions.
    /// </summary>
    /// <param name="externalId">The external ID of the manifest to disable.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// The manifest is not deleted, only disabled. Use <see cref="EnableAsync"/>
    /// to re-enable the job.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no manifest with the specified ExternalId exists.
    /// </exception>
    Task DisableAsync(string externalId, CancellationToken ct = default);

    /// <summary>
    /// Enables a previously disabled scheduled job.
    /// </summary>
    /// <param name="externalId">The external ID of the manifest to enable.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no manifest with the specified ExternalId exists.
    /// </exception>
    Task EnableAsync(string externalId, CancellationToken ct = default);

    /// <summary>
    /// Triggers immediate execution of a scheduled job.
    /// </summary>
    /// <param name="externalId">The external ID of the manifest to trigger.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// This creates a new execution independent of the regular schedule.
    /// The job's normal schedule continues unaffected. The run is one someone asked for by name,
    /// so it runs even while the manifest is disabled (a disabled manifest group still holds it).
    /// A manifest holds at most one queued work queue entry, so when it already has one, nothing
    /// more is queued and that entry becomes the triggered run: it is marked as asked for by name,
    /// which releases it if the manifest is disabled, and an entry due later (a retry waiting out
    /// its backoff, or a delayed trigger) is brought forward to now. The log says which happened.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no manifest with the specified ExternalId exists.
    /// </exception>
    Task TriggerAsync(string externalId, CancellationToken ct = default);

    /// <summary>
    /// Triggers a delayed execution of a scheduled job. The job will be dispatched
    /// after the specified delay, independent of its normal schedule.
    /// </summary>
    /// <param name="externalId">The external ID of the manifest to trigger.</param>
    /// <param name="delay">The delay before the job should be dispatched.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Creates a WorkQueue entry with <c>ScheduledAt = DateTime.UtcNow + delay</c>.
    /// The JobDispatcher will skip the entry until <c>ScheduledAt &lt;= now</c>.
    /// The manifest's normal schedule continues unaffected. Like the immediate trigger, the run
    /// is one someone asked for by name and runs even while the manifest is disabled. When the
    /// manifest already has a queued entry, nothing more is queued and that entry is marked as
    /// asked for by name; if it is due later than <c>DateTime.UtcNow + delay</c> it is brought
    /// forward to that time, and if it is due sooner it keeps its own time.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no manifest with the specified ExternalId exists.
    /// </exception>
    Task TriggerAsync(string externalId, TimeSpan delay, CancellationToken ct = default);

    /// <summary>
    /// Creates a one-off manifest that fires once after the specified delay, then auto-disables.
    /// An external ID is auto-generated.
    /// </summary>
    /// <typeparam name="TTrain">The train interface type.</typeparam>
    /// <typeparam name="TInput">The input type for the train.</typeparam>
    /// <typeparam name="TOutput">The output type of <typeparamref name="TTrain"/>, from its <c>IServiceTrain&lt;TInput, TOutput&gt;</c> interface.</typeparam>
    /// <param name="input">The input data for the train execution.</param>
    /// <param name="delay">The delay before the job should execute.</param>
    /// <param name="options">Optional callback to configure manifest options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created manifest. Use <c>ExternalId</c> to reference it later.</returns>
    Task<Manifest> ScheduleOnceAsync<TTrain, TInput, TOutput>(
        TInput input,
        TimeSpan delay,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties;

    /// <summary>
    /// Creates a one-off manifest with an explicit external ID that fires once after the
    /// specified delay, then auto-disables.
    /// </summary>
    /// <typeparam name="TTrain">The train interface type.</typeparam>
    /// <typeparam name="TInput">The input type for the train.</typeparam>
    /// <typeparam name="TOutput">The output type of <typeparamref name="TTrain"/>, from its <c>IServiceTrain&lt;TInput, TOutput&gt;</c> interface.</typeparam>
    /// <param name="externalId">A unique identifier for this one-off job.</param>
    /// <param name="input">The input data for the train execution.</param>
    /// <param name="delay">The delay before the job should execute.</param>
    /// <param name="options">Optional callback to configure manifest options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created or updated manifest.</returns>
    Task<Manifest> ScheduleOnceAsync<TTrain, TInput, TOutput>(
        string externalId,
        TInput input,
        TimeSpan delay,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties;

    /// <summary>
    /// Triggers immediate execution of all eligible manifests in a manifest group.
    /// </summary>
    /// <param name="groupId">The ID of the manifest group to trigger.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The number of manifests that were queued. A manifest that already has a queued entry is
    /// skipped, not counted, and does not stop the others being queued; the skipped count is
    /// logged.
    /// </returns>
    /// <remarks>
    /// Only enabled manifests with non-dependent schedule types (None, Cron, Interval, OnDemand)
    /// are queued, each entry marked as asked for by name the way <see cref="TriggerAsync(string, CancellationToken)"/>
    /// marks one, so disabling a manifest after the group trigger does not hold its run. A member
    /// that already has a queued entry is not counted, but that entry is marked the same way and
    /// brought forward to now if it was due later. Dependent and DormantDependent manifests are skipped because they rely on
    /// parent completion and may lack standalone inputs.
    /// </remarks>
    Task<int> TriggerGroupAsync(long groupId, CancellationToken ct = default);

    /// <summary>
    /// Cancels all pending and running executions of a scheduled job.
    /// </summary>
    /// <param name="externalId">The external ID of the manifest whose executions should be cancelled.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of metadata records that had cancellation requested.</returns>
    /// <remarks>
    /// Sets <c>CancellationRequested = true</c> on all Pending and InProgress metadata for the
    /// manifest (cross-server, picked up at next junction boundary via CancellationCheckProvider)
    /// and also attempts same-server instant cancellation via <see cref="ICancellationRegistry"/>.
    /// A Pending run is recorded <see cref="TrainState.Cancelled"/> without running when the job
    /// runner picks it up, on any host. The same rule <c>IOperationsService.CancelExecutionsAsync</c> applies to a list of runs.
    /// Cancelled trains transition to <see cref="TrainState.Cancelled"/> and are not retried.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no manifest with the specified ExternalId exists.
    /// </exception>
    Task<int> CancelAsync(string externalId, CancellationToken ct = default);

    /// <summary>
    /// Cancels all pending and running executions for all manifests in a manifest group.
    /// </summary>
    /// <param name="groupId">The ID of the manifest group whose executions should be cancelled.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of metadata records that had cancellation requested.</returns>
    /// <remarks>
    /// Sets <c>CancellationRequested = true</c> on all Pending and InProgress metadata for
    /// manifests in the group and attempts same-server instant cancellation via
    /// <see cref="ICancellationRegistry"/>, the rule <c>IOperationsService.CancelExecutionsAsync</c>
    /// applies to a list of runs. A Pending run is recorded <see cref="TrainState.Cancelled"/>
    /// without running when the job runner picks it up, on any host.
    /// </remarks>
    Task<int> CancelGroupAsync(long groupId, CancellationToken ct = default);

    #region Dead Letter Operations

    /// <summary>
    /// Requeues a single dead letter, creating a new WorkQueue entry for retry.
    /// </summary>
    /// <param name="deadLetterId">The ID of the dead letter to requeue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success and the new WorkQueue ID.</returns>
    /// <remarks>
    /// Only dead letters in <see cref="DeadLetterStatus.AwaitingIntervention"/> status
    /// can be requeued. The dead letter's failure counter is reset, allowing the manifest
    /// to resume normal scheduling. A manifest may have one queued entry at a time, so when it
    /// already has one the result is a failure and the dead letter is left awaiting intervention.
    /// </remarks>
    Task<DeadLetterOperationResult> RequeueDeadLetterAsync(
        long deadLetterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Acknowledges a single dead letter without retrying.
    /// </summary>
    /// <param name="deadLetterId">The ID of the dead letter to acknowledge.</param>
    /// <param name="note">A note explaining the acknowledgement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success.</returns>
    Task<DeadLetterOperationResult> AcknowledgeDeadLetterAsync(
        long deadLetterId,
        string note,
        CancellationToken ct = default
    );

    /// <summary>
    /// Requeues multiple dead letters by ID.
    /// </summary>
    /// <param name="deadLetterIds">The IDs of the dead letters to requeue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of dead letters resolved, and a message that also counts the folded and skipped ones.</returns>
    /// <remarks>
    /// At most one work queue entry is created per manifest. A dead letter whose manifest already
    /// has a queued entry is skipped and left awaiting intervention; dead letters that share a
    /// manifest are folded into one entry and all resolved, since a requeue runs the manifest's
    /// own properties. An empty list, or one longer than <c>OperationsService.MaxBatchSize</c>
    /// (1000) ids, is refused: the result counts nothing and its message says why.
    /// </remarks>
    Task<BatchDeadLetterResult> RequeueDeadLettersAsync(
        long[] deadLetterIds,
        CancellationToken ct = default
    );

    /// <summary>
    /// Acknowledges multiple dead letters by ID.
    /// </summary>
    /// <param name="deadLetterIds">The IDs of the dead letters to acknowledge.</param>
    /// <param name="note">A note explaining the acknowledgement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of dead letters successfully acknowledged.</returns>
    /// <remarks>
    /// An empty list, or one longer than <c>OperationsService.MaxBatchSize</c> (1000) ids, is
    /// refused: the result counts nothing and its message says why.
    /// </remarks>
    Task<BatchDeadLetterResult> AcknowledgeDeadLettersAsync(
        long[] deadLetterIds,
        string note,
        CancellationToken ct = default
    );

    /// <summary>
    /// Requeues all dead letters in AwaitingIntervention status.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of dead letters resolved, and a message that also counts the folded and skipped ones.</returns>
    /// <remarks>
    /// Creates at most one entry per manifest, as <see cref="RequeueDeadLettersAsync"/> does. The
    /// dead letters are read and requeued a page of manifests at a time, so a large backlog is
    /// never loaded at once; every dead letter for one manifest is in the same page.
    /// </remarks>
    Task<BatchDeadLetterResult> RequeueAllDeadLettersAsync(CancellationToken ct = default);

    /// <summary>
    /// Acknowledges all dead letters in AwaitingIntervention status.
    /// </summary>
    /// <param name="note">A note explaining the acknowledgement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of dead letters acknowledged.</returns>
    Task<BatchDeadLetterResult> AcknowledgeAllDeadLettersAsync(
        string note,
        CancellationToken ct = default
    );

    #endregion
}
