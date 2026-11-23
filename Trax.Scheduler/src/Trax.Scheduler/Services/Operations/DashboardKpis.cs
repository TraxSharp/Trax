namespace Trax.Scheduler.Services.Operations;

/// <param name="ExecutionsToday">Total executions started today (UTC), all states.</param>
/// <param name="SuccessRate">Completed / (Completed + Failed) as a percentage. Zero when no terminal executions.</param>
/// <param name="CurrentlyRunning">Executions in <c>InProgress</c> state right now.</param>
/// <param name="UnresolvedDeadLetters">Dead letters in <c>AwaitingIntervention</c> state.</param>
public record DashboardKpis(
    int ExecutionsToday,
    double SuccessRate,
    int CurrentlyRunning,
    int UnresolvedDeadLetters
);
