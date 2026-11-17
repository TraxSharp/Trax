namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// A train failing more often in the last hour than over the week before it. A rate is failed runs
/// over finished runs (completed or failed); cancelled runs count in neither.
/// </summary>
/// <param name="TrainName">The train (the train interface's full name).</param>
/// <param name="FailedLastHour">Runs that started in the last hour and failed.</param>
/// <param name="FinishedLastHour">Runs that started in the last hour and completed or failed.</param>
/// <param name="LastHourFailureRate">The last hour's failure rate, 0 to 1.</param>
/// <param name="BaselineFailureRate">
/// The failure rate of runs that started in the seven days before the last hour, 0 to 1; 0 when
/// none finished.
/// </param>
/// <param name="Link">The console's runs, filtered to this train's failed runs.</param>
public sealed record AttentionFailingTrain(
    string TrainName,
    long FailedLastHour,
    long FinishedLastHour,
    double LastHourFailureRate,
    double BaselineFailureRate,
    string Link
);
