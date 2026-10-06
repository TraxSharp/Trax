namespace Trax.Samples.Recovery;

/// <summary>
/// How slowly the demo runs, so a person can watch it. Bound from the <c>Recovery</c> configuration
/// section; the E2E suite shortens both.
/// </summary>
public sealed class DemoPace
{
    public const string Section = "Recovery";

    /// <summary>How long each junction takes.</summary>
    public TimeSpan StepDelay { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>The shortest time the demo model takes to answer.</summary>
    public TimeSpan ModelLatencyMin { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The longest time the demo model takes to answer.</summary>
    public TimeSpan ModelLatencyMax { get; set; } = TimeSpan.FromMilliseconds(1500);
}
