namespace Trax.Dashboard.Tests.Integration.Fakes.Time;

/// <summary>A clock that moves only when the test moves it.</summary>
public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
