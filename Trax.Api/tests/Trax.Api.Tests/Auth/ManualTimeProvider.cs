namespace Trax.Api.Tests.Auth;

/// <summary>
/// A clock the test moves by hand. Timers fire synchronously inside <see cref="Advance"/>, in
/// order, and a timer whose callback sets it due again within the advanced span fires again.
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period
    )
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_timers)
            _timers.Add(timer);
        return timer;
    }

    /// <summary>Moves the clock forward, firing every timer that falls due on the way.</summary>
    public void Advance(TimeSpan by)
    {
        var target = _now + by;
        while (true)
        {
            ManualTimer[] timers;
            lock (_timers)
                timers = [.. _timers];

            var next = timers
                .Where(t => t.Due is { } due && due <= target)
                .OrderBy(t => t.Due)
                .FirstOrDefault();
            if (next is null)
                break;

            if (next.Due > _now)
                _now = next.Due!.Value;
            next.Fire();
        }
        _now = target;
    }

    private sealed class ManualTimer(
        ManualTimeProvider owner,
        TimerCallback callback,
        object? state
    ) : ITimer
    {
        public DateTimeOffset? Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            return true;
        }

        public void Fire()
        {
            Due = null;
            callback(state);
        }

        public void Dispose() => Due = null;

        public ValueTask DisposeAsync()
        {
            Due = null;
            return ValueTask.CompletedTask;
        }
    }
}
