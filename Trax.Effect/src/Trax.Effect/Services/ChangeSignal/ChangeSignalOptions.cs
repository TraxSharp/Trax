namespace Trax.Effect.Services.ChangeSignal;

/// <summary>
/// Tuning for the change-signal pipeline. Registered as a singleton by <c>AddTrax()</c>;
/// override by registering a configured instance before <c>AddTrax()</c> runs.
/// </summary>
public sealed class ChangeSignalOptions
{
    /// <summary>
    /// The size of the signal buffer. The buffer holds each pending domain once, so it never
    /// needs more room than there are <see cref="ChangeDomain"/> values, and a smaller value is
    /// raised to that. Repeats of a domain still waiting to be read are absorbed rather than
    /// buffered, so no burst can fill it.
    /// </summary>
    public int ChannelCapacity { get; set; } = 1024;

    /// <summary>
    /// How long the coalescer keeps collecting signals after the first arrival before flushing
    /// one signal per distinct domain. Larger windows coalesce more aggressively at the cost of
    /// a little latency; the dashboard's client-side debounce stacks on top of this.
    /// </summary>
    public TimeSpan CoalesceWindow { get; set; } = TimeSpan.FromMilliseconds(250);
}
