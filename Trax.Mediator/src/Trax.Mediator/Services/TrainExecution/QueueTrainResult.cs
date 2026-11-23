namespace Trax.Mediator.Services.TrainExecution;

/// <summary>The work queue entry <c>ITrainExecutionService.QueueAsync</c> created.</summary>
/// <param name="WorkQueueId">The entry's database id.</param>
/// <param name="ExternalId">
/// The entry's external id, a 32-character hex GUID. The run the scheduler later dispatches
/// carries the same external id, and the train's <c>OnQueue</c> hook saw it too, so it
/// correlates the enqueue with its run.
/// </param>
public record QueueTrainResult(long WorkQueueId, string ExternalId);
