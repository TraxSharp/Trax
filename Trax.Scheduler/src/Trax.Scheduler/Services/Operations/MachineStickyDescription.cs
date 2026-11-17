namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// One sticky: its flow role (<c>"actor"</c>, <c>"command"</c>, <c>"event"</c>, <c>"policy"</c>, <c>"aggregate"</c> or
/// <c>"view"</c>), the lane it is in, its label, and the scenarios that produced it. A host file with no provenance gives
/// none; a scenario is its file, line and name, never its steps.
/// </summary>
public sealed record MachineStickyDescription(
    string Id,
    string Kind,
    string Machine,
    string Label,
    IReadOnlyList<MachineScenarioDescription> Scenarios
);
