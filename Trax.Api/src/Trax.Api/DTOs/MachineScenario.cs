using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>A scenario behind a sticky: its feature file, line and name, never its steps.</summary>
public record MachineScenario(string File, long Line, string Scenario);
