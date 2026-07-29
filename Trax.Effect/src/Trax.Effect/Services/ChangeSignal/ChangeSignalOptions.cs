namespace Trax.Effect.Services.ChangeSignal;

/// <summary>
/// Tuning for the change-signal pipeline. Registered as a singleton by <c>AddTrax()</c>;
/// override by registering a configured instance before <c>AddTrax()</c> runs.
/// </summary>
public sealed class ChangeSignalOptions
{
    /// <summary>
    /// Maximum number of buffered signals before new ones are dropped. Signals are a single
    /// enum, so a small buffer absorbs any realistic burst; the point of the bound is to keep
    /// a runaway producer from growing memory without limit.
    /// </summary>
    public int ChannelCapacity { get; set; } = 1024;

    /// <summary>
    /// How long the coalescer keeps collecting signals after the first arrival before flushing
    /// one signal per distinct domain. Larger windows coalesce more aggressively at the cost of
    /// a little latency; the dashboard's client-side debounce stacks on top of this.
    /// </summary>
    public TimeSpan CoalesceWindow { get; set; } = TimeSpan.FromMilliseconds(250);
}
