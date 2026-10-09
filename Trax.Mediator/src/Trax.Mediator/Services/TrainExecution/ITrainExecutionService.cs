namespace Trax.Mediator.Services.TrainExecution;

/// <summary>
/// Provides train execution via two paths: queueing for async scheduler dispatch
/// or direct synchronous execution via ITrainBus.
/// </summary>
public interface ITrainExecutionService
{
    /// <summary>
    /// Queues a train for asynchronous execution via WorkQueue.
    /// The scheduler picks it up and dispatches it on its own machine.
    /// </summary>
    /// <param name="trainName">The fully qualified service type name of the train.</param>
    /// <param name="inputJson">JSON-serialized input for the train. Null or blank is read as an empty object, which is refused with <see cref="System.Text.Json.JsonException"/> when the input type needs values: a constructor parameter with no default, or a <c>required</c> member.</param>
    /// <param name="priority">Dispatch priority (0–31, higher runs first).</param>
    /// <param name="scheduledAt">Earliest time the entry may be dispatched, stored as UTC: a local time is converted and an unspecified kind is taken as UTC. Null dispatches as soon as a worker is free.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created WorkQueue entry's ID and external ID.</returns>
    /// <exception cref="Exceptions.TrainNotFoundException">No registered train has that name.</exception>
    /// <exception cref="Exceptions.AmbiguousTrainNameException">The name matches more than one registered train.</exception>
    /// <exception cref="UnauthorizedAccessException">The train's <c>[TraxAuthorize]</c> requirements refuse the caller (<c>TrainAuthorizationException</c> is one).</exception>
    /// <exception cref="Exceptions.TrainInputValidationException">The input is larger than <c>MaxInputJsonBytes</c>.</exception>
    /// <exception cref="System.Text.Json.JsonException">The input is not valid JSON for the train's input type, is the JSON literal <c>null</c>, or is missing and the input type needs values.</exception>
    /// <exception cref="InvalidOperationException">The train declares <c>[TraxAuthorize]</c> and no <c>ITrainAuthorizationService</c> is registered, outside a trusted scope; or its <c>QueueSubjectKey</c> returned a key <c>WorkQueue.Create</c> refuses: empty or whitespace, containing an unpaired surrogate, or longer than <c>WorkQueue.MaxSubjectKeyLength</c> Unicode characters.</exception>
    /// <exception cref="Exceptions.QueuedWorkCancelledException">The train defers promotion and its staged entry was cancelled while its <c>OnQueue</c> hook ran. The work will not run, but the hook's side-effect may have landed.</exception>
    /// <remarks>
    /// An exception thrown by the train's <c>OnQueue</c> hook or <c>QueueSubjectKey</c>
    /// propagates as it was thrown, and no entry is left behind.
    /// </remarks>
    Task<QueueTrainResult> QueueAsync(
        string trainName,
        string? inputJson,
        int priority = 0,
        DateTime? scheduledAt = null,
        CancellationToken ct = default
    );

    /// <summary>
    /// Queues a train as <see cref="QueueAsync(string, string?, int, DateTime?, CancellationToken)"/>
    /// does, with the options that overload has no parameter for.
    /// </summary>
    /// <param name="trainName">The fully qualified service type name of the train.</param>
    /// <param name="inputJson">JSON-serialized input for the train, read as the other overload reads it.</param>
    /// <param name="options">Priority, schedule, and the earlier run whose decisions the new run replays.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created WorkQueue entry's ID and external ID.</returns>
    /// <exception cref="Exceptions.DecisionReplayNotSupportedException">
    /// <see cref="QueueTrainOptions.ReplayDecisionsOf"/> is set and this implementation does not
    /// implement this overload, so it predates replaying decisions. The host is misconfigured
    /// rather than the enqueue refused: the exception names the implementation, and the enqueue is
    /// not made without the link, because the run would then ask its deciders afresh and could
    /// take a different track from the run it repeats.
    /// </exception>
    /// <remarks>Throws whatever the other overload throws, for the same reasons.</remarks>
    Task<QueueTrainResult> QueueAsync(
        string trainName,
        string? inputJson,
        QueueTrainOptions options,
        CancellationToken ct = default
    ) =>
        options.ReplayDecisionsOf is null
            ? QueueAsync(trainName, inputJson, options.Priority, options.ScheduledAt, ct)
            : throw new Exceptions.DecisionReplayNotSupportedException(GetType());

    /// <summary>
    /// Runs a train directly via ITrainBus on this machine.
    /// This is a blocking call that awaits train completion.
    /// </summary>
    /// <param name="trainName">The fully qualified service type name of the train.</param>
    /// <param name="inputJson">JSON-serialized input for the train. Blank is read the way <see cref="QueueAsync(string, string?, int, DateTime?, CancellationToken)"/> reads a missing input.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The metadata ID of the completed execution.</returns>
    /// <exception cref="Exceptions.TrainNotFoundException">No registered train has that name.</exception>
    /// <exception cref="Exceptions.AmbiguousTrainNameException">The name matches more than one registered train.</exception>
    /// <exception cref="UnauthorizedAccessException">The train's <c>[TraxAuthorize]</c> requirements refuse the caller (<c>TrainAuthorizationException</c> is one).</exception>
    /// <exception cref="Exceptions.TrainInputValidationException">The input is larger than <c>MaxInputJsonBytes</c>.</exception>
    /// <exception cref="System.Text.Json.JsonException">The input is not valid JSON for the train's input type, is the JSON literal <c>null</c>, or is missing and the input type needs values.</exception>
    /// <exception cref="InvalidOperationException">The train declares <c>[TraxAuthorize]</c> and no <c>ITrainAuthorizationService</c> is registered, outside a trusted scope.</exception>
    Task<RunTrainResult> RunAsync(
        string trainName,
        string inputJson,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resolves a train by name, authorizes the current caller for it, and reads the caller's
    /// input into the train's input type: the steps <see cref="QueueAsync(string, string?, int, DateTime?, CancellationToken)"/> and
    /// <see cref="RunAsync"/> take before doing anything else, for a surface that submits the work
    /// some other way. Authorization runs before the input is read, so a caller who may not use the
    /// train learns nothing about its input from a parse error. Nothing is written.
    /// </summary>
    /// <param name="trainName">The fully qualified service type name of the train, or its friendly name when that is unique.</param>
    /// <param name="inputJson">JSON input, read exactly as <see cref="QueueAsync(string, string?, int, DateTime?, CancellationToken)"/> reads it: null or blank as an empty object, property names in any case, a repeated property refused, and the <c>MaxInputJsonBytes</c> cap applied.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The train and its input. Only this method can produce one.</returns>
    /// <exception cref="Exceptions.TrainNotFoundException">No registered train has that name.</exception>
    /// <exception cref="Exceptions.AmbiguousTrainNameException">The name matches more than one registered train.</exception>
    /// <exception cref="UnauthorizedAccessException">The train's <c>[TraxAuthorize]</c> requirements refuse the caller.</exception>
    /// <exception cref="Exceptions.TrainInputValidationException">The input is larger than <c>MaxInputJsonBytes</c>.</exception>
    /// <exception cref="System.Text.Json.JsonException">The input is not valid JSON for the train's input type, names a property twice, is the JSON literal <c>null</c>, or is missing and the input type needs values.</exception>
    /// <exception cref="InvalidOperationException">The train declares <c>[TraxAuthorize]</c> and no <c>ITrainAuthorizationService</c> is registered, outside a trusted scope.</exception>
    /// <exception cref="NotSupportedException">This implementation predates the method. It refuses rather than skip authorization.</exception>
    Task<PreparedTrain> PrepareAsync(
        string trainName,
        string? inputJson,
        CancellationToken ct = default
    ) =>
        throw new NotSupportedException(
            $"{GetType().Name} does not implement PrepareAsync. Resolve ITrainExecutionService "
                + "from dependency injection to get the mediator's implementation."
        );
}

/// <summary>The work queue entry <c>ITrainExecutionService.QueueAsync</c> created.</summary>
/// <param name="WorkQueueId">The entry's database id.</param>
/// <param name="ExternalId">
/// The entry's external id, a 32-character hex GUID. The run the scheduler later dispatches
/// carries the same external id, and the train's <c>OnQueue</c> hook saw it too, so it
/// correlates the enqueue with its run.
/// </param>
public record QueueTrainResult(long WorkQueueId, string ExternalId);

/// <summary>
/// How a train is queued, beyond its name and input.
/// </summary>
public sealed record QueueTrainOptions
{
    /// <summary>Dispatch priority (0–31, higher runs first).</summary>
    public int Priority { get; init; }

    /// <summary>
    /// Earliest time the entry may be dispatched, stored as UTC. Null dispatches as soon as a
    /// worker is free.
    /// </summary>
    public DateTime? ScheduledAt { get; init; }

    /// <summary>
    /// The metadata id of an earlier run whose recorded decisions the new run replays, so it takes
    /// the tracks that run took instead of asking its deciders again. Set when repeating a run, by
    /// the operations service only, which checks the run it names first; an implementation reads it.
    /// </summary>
    public long? ReplayDecisionsOf { get; internal init; }

    /// <summary>
    /// The metadata id of a failed run the new run resumes, skipping to the step after its latest
    /// checkpoint (or to <see cref="ResumeAt"/>) instead of running every step again. See
    /// Trax.Docs/adr/0047. Set by the operations service only, which checks the run it names
    /// first; the resumed run checks it again as it starts.
    /// </summary>
    public long? ResumeFrom { get; internal init; }

    /// <summary>The step the new run resumes at, or null for after the latest checkpoint.</summary>
    public string? ResumeAt { get; internal init; }
}

/// <summary>A completed run from <see cref="ITrainExecutionService.RunAsync"/> or <c>IRunExecutor</c>.</summary>
/// <param name="MetadataId">The id of the run's metadata record.</param>
/// <param name="ExternalId">The run's external id.</param>
/// <param name="Output">
/// The train's output, typed as its <c>TOut</c>; null for a train whose output is <c>Unit</c>.
/// </param>
public record RunTrainResult(long MetadataId, string ExternalId, object? Output = null);
