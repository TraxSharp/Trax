namespace Trax.Scheduler.Sandbox.Tests.Fakes;

/// <summary>The system clock moved by <see cref="Shift"/>; its timers are the system's, so a delay still passes.</summary>
public sealed class ShiftedClock : TimeProvider
{
    public TimeSpan Shift { get; set; }

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Shift;
}
