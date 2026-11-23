namespace Trax.Scheduler.Services.TraxScheduler;

/// <summary>
/// Result of a single dead letter operation (requeue or acknowledge).
/// </summary>
public record DeadLetterOperationResult(bool Success, long? WorkQueueId, string Message);
