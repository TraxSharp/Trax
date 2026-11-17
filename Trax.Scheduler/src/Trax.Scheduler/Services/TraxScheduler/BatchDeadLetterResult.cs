namespace Trax.Scheduler.Services.TraxScheduler;

/// <summary>
/// Result of a batch dead letter operation.
/// </summary>
public record BatchDeadLetterResult(int Count, string Message);
