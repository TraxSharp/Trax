namespace Trax.Scheduler.Services.SchedulerLiveness;

/// <summary>
/// Tracks when the JobDispatcher last completed a polling cycle so a wedged scheduler
/// (process alive, dispatching nothing) can be told apart from a healthy one. Wire it into
/// an ASP.NET health check with <c>AddHealthChecks().AddTraxSchedulerLiveness()</c>.
/// </summary>
public interface ISchedulerLivenessMonitor
{
    /// <summary>
    /// When the monitor was created (scheduler startup). Used as the liveness baseline
    /// before the first dispatch cycle completes, so a cold start is healthy within the
    /// grace window but a scheduler that never dispatches still trips.
    /// </summary>
    DateTimeOffset StartedAt { get; }

    /// <summary>
    /// When the JobDispatcher last completed a polling cycle, or null if it has not
    /// completed one since startup.
    /// </summary>
    DateTimeOffset? LastDispatchCompletedAt { get; }

    /// <summary>Records that the JobDispatcher just completed a polling cycle.</summary>
    void RecordDispatchCycle();
}

internal sealed class SchedulerLivenessMonitor : ISchedulerLivenessMonitor
{
    private readonly TimeProvider _timeProvider;
    private long _lastDispatchTicks;

    public SchedulerLivenessMonitor(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        StartedAt = timeProvider.GetUtcNow();
    }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? LastDispatchCompletedAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastDispatchTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public void RecordDispatchCycle() =>
        Interlocked.Exchange(ref _lastDispatchTicks, _timeProvider.GetUtcNow().UtcTicks);
}
