namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// One bucket of the executions-over-time chart, broken down by terminal state.
/// </summary>
/// <param name="Timestamp">UTC start of the bucket.</param>
/// <param name="Completed">Count of completed executions in this bucket.</param>
/// <param name="Failed">Count of failed executions.</param>
/// <param name="Cancelled">Count of cancelled executions.</param>
public record ExecutionsBucket(DateTime Timestamp, int Completed, int Failed, int Cancelled);
