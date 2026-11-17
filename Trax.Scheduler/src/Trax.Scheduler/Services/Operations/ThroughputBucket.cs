namespace Trax.Scheduler.Services.Operations;

/// <param name="Timestamp">UTC start of the bucket.</param>
/// <param name="Count">Number of completed executions in the bucket.</param>
public record ThroughputBucket(DateTime Timestamp, int Count);
