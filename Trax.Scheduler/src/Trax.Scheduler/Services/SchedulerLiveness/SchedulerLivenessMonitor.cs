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
}

/// <summary>
/// The monitor, with its one writer. Only the JobDispatcher stamps a cycle: the interface above is
/// what anything else resolves, and it has no way to report the scheduler alive.
/// </summary>
internal sealed class SchedulerLivenessMonitor : ISchedulerLivenessMonitor
{
    private readonly TimeProvider _timeProvider;
    private long _lastDispatchTicks;
    private long _cycleStartedTicks;
    private int _lastCycleFailed;

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

    /// <summary>
    /// When the cycle now running began, or null between cycles. Null too while the last cycle to
    /// finish failed: a cycle that follows a failure proves nothing until it completes, so only a
    /// cycle after a success keeps the scheduler live while it runs (a slow synchronous dispatch).
    /// </summary>
    public DateTimeOffset? RunningCycleStartedAt
    {
        get
        {
            if (Volatile.Read(ref _lastCycleFailed) == 1)
                return null;
            var ticks = Interlocked.Read(ref _cycleStartedTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>Records that the JobDispatcher began a polling cycle.</summary>
    public void BeginDispatchCycle() =>
        Interlocked.Exchange(ref _cycleStartedTicks, _timeProvider.GetUtcNow().UtcTicks);

    /// <summary>Records that the JobDispatcher just completed a polling cycle.</summary>
    public void RecordDispatchCycle()
    {
        Interlocked.Exchange(ref _lastDispatchTicks, _timeProvider.GetUtcNow().UtcTicks);
        Volatile.Write(ref _lastCycleFailed, 0);
        Interlocked.Exchange(ref _cycleStartedTicks, 0);
    }

    /// <summary>
    /// Records that a polling cycle threw. The completion time stays where it was, so a dispatcher
    /// that keeps failing goes stale and the health check reports it.
    /// </summary>
    public void RecordDispatchCycleFailed()
    {
        Volatile.Write(ref _lastCycleFailed, 1);
        Interlocked.Exchange(ref _cycleStartedTicks, 0);
    }
}
