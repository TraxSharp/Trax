namespace Trax.Scheduler.Services.Operations;

/// <param name="TrainName">Train interface FullName as stored on <c>Metadata.Name</c>.</param>
/// <param name="Count">Number of failures over the time window (last 7 days).</param>
public record TrainFailureCount(string TrainName, int Count);
