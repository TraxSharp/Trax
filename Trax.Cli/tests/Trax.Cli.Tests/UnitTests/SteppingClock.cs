namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// A clock that never waits: a delay moves the time forward by its length and ends at once, so a poll loop with a
/// thirty-minute deadline runs to it in milliseconds.
/// </summary>
internal sealed class SteppingClock : TimeProvider
{
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly Lock _gate = new();
    private DateTimeOffset _now = Start;

    /// <summary>How far the delays have moved the time.</summary>
    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate)
                return _now - Start;
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period
    )
    {
        lock (_gate)
            _now += dueTime;
        ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new Fired();
    }

    private sealed class Fired : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
