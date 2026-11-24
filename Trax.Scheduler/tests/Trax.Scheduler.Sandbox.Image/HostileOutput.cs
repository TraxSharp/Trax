namespace Trax.Scheduler.Sandbox.Image;

/// <summary>What the hostile train managed. Every list should come back empty from a sandbox.</summary>
/// <param name="Reached">Probes it connected to, and names it resolved.</param>
/// <param name="Written">Paths outside its output and scratch directories it wrote.</param>
/// <param name="Echo">For the benign act, its input.</param>
public sealed record HostileOutput(
    IReadOnlyList<string> Reached,
    IReadOnlyList<string> Written,
    string Echo
);
