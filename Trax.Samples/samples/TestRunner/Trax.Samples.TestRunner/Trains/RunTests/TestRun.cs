using Trax.Samples.TestRunner.Models;

namespace Trax.Samples.TestRunner.Trains.RunTests;

/// <summary>A run of a project the registry discovered, never one the caller described.</summary>
public record TestRun
{
    public required TestProject Project { get; init; }
    public bool Build { get; init; } = true;
}
