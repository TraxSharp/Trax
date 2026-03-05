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
    /// <param name="inputJson">JSON-serialized input for the train.</param>
    /// <param name="priority">Dispatch priority (0–31, higher runs first).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created WorkQueue entry's ID and external ID.</returns>
    Task<QueueTrainResult> QueueAsync(
        string trainName,
        string inputJson,
        int priority = 0,
        CancellationToken ct = default
    );

    /// <summary>
    /// Runs a train directly via ITrainBus on this machine.
    /// This is a blocking call that awaits train completion.
    /// </summary>
    /// <param name="trainName">The fully qualified service type name of the train.</param>
    /// <param name="inputJson">JSON-serialized input for the train.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The metadata ID of the completed execution.</returns>
    Task<RunTrainResult> RunAsync(
        string trainName,
        string inputJson,
        CancellationToken ct = default
    );
}

public record QueueTrainResult(long WorkQueueId, string ExternalId);

public record RunTrainResult(long MetadataId, object? Output = null);
