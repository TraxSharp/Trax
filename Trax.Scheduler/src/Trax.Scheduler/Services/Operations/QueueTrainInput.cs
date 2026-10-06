namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Request describing a train to queue for execution.
/// Shared input record used by both the GraphQL <c>operations.workQueue.queueTrain</c>
/// mutation and the dashboard's QueueTrainDialog so both surfaces have identical
/// validation and persistence semantics.
/// </summary>
/// <param name="TrainName">
/// Fully qualified name of the train interface (matches what
/// <c>operations.getTrains</c> returns as <c>ServiceTypeName</c>).
/// </param>
/// <param name="InputJson">
/// JSON payload that deserializes to the train's input type. <c>null</c> or blank is read as
/// an empty object, so a train whose input needs no values can be queued without one.
/// </param>
/// <param name="Priority">Dispatch priority 0-31. Higher values run first. Defaults to 0.</param>
/// <param name="ScheduledAt">
/// Earliest UTC time the entry should be picked up. <c>null</c> means dispatch immediately.
/// </param>
public record QueueTrainInput(
    string TrainName,
    string? InputJson = null,
    int Priority = 0,
    DateTime? ScheduledAt = null
);
