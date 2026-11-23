namespace Trax.Scheduler.Services.Operations;

/// <param name="TrainName">Train interface FullName.</param>
/// <param name="AverageMilliseconds">Mean execution time over completed root-level runs in the last 7 days.</param>
public record TrainAverageDuration(string TrainName, double AverageMilliseconds);
