namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>A clock that moves only when a test moves it.</summary>
public sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
