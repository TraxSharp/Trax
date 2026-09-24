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
    /// <exception cref="InvalidOperationException">The train declares <c>[TraxAuthorize]</c> and no <c>ITrainAuthorizationService</c> is registered, outside a trusted scope; or its <c>QueueSubjectKey</c> returned an empty key or one longer than <c>WorkQueue.MaxSubjectKeyLength</c>.</exception>
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
    /// Runs a train directly via ITrainBus on this machine.
    /// This is a blocking call that awaits train completion.
    /// </summary>
    /// <param name="trainName">The fully qualified service type name of the train.</param>
    /// <param name="inputJson">JSON-serialized input for the train. Blank is read the way <see cref="QueueAsync"/> reads a missing input.</param>
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
}

public record QueueTrainResult(long WorkQueueId, string ExternalId);

public record RunTrainResult(long MetadataId, string ExternalId, object? Output = null);
