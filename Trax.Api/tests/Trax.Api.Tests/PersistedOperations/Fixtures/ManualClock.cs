namespace Trax.Api.Tests.PersistedOperations.Fixtures;

/// <summary>
/// A <see cref="TimeProvider"/> that moves only when a test advances it, for the caches' maximum
/// age and TTL.
/// </summary>
public sealed class ManualClock : TimeProvider
{
    private readonly DateTimeOffset _start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long _elapsedTicks;

    public void Advance(TimeSpan by) => Interlocked.Add(ref _elapsedTicks, by.Ticks);

    public override DateTimeOffset GetUtcNow() =>
        _start + TimeSpan.FromTicks(Interlocked.Read(ref _elapsedTicks));

    public override long GetTimestamp() => Interlocked.Read(ref _elapsedTicks);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
}
