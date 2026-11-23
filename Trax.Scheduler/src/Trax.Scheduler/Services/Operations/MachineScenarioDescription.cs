namespace Trax.Scheduler.Services.Operations;

/// <summary>A scenario that produced a sticky: its feature file, its line and its name.</summary>
public sealed record MachineScenarioDescription(string File, long Line, string Scenario);
