namespace Trax.Scheduler.Sandbox.Image;

/// <summary>What the hostile train is asked to do.</summary>
/// <param name="Act">One of <see cref="HostileJunction"/>'s acts.</param>
/// <param name="Targets">The addresses to probe, the paths to write, or the text to echo.</param>
/// <param name="Count">How many files, or MiB, the act makes.</param>
public sealed record HostileInput(string Act, IReadOnlyList<string> Targets, int Count = 0);
