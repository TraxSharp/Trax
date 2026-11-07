namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// The event-storming board of a machine served from features, as <see cref="IOperationsService.GetMachineBoardAsync"/>
/// draws it (ADR <c>Trax.Docs/adr/0058</c>): one lane per machine, the machine's own and each machine a policy or a view
/// joins it to, read from the IRs and the host file the host serves.
/// </summary>
/// <param name="Machine">The machine the board was asked for.</param>
/// <param name="Lanes">The machines on the board, the asked-for one first, the rest by name.</param>
/// <param name="Stickies">Every sticky, by lane and flow role.</param>
/// <param name="Links">Every arrow between two stickies on the board.</param>
public sealed record MachineBoardDescription(
    string Machine,
    IReadOnlyList<string> Lanes,
    IReadOnlyList<MachineStickyDescription> Stickies,
    IReadOnlyList<MachineBoardLinkDescription> Links
);

/// <summary>
/// One sticky: its flow role (<c>actor</c>, <c>command</c>, <c>event</c>, <c>policy</c>, <c>aggregate</c> or
/// <c>view</c>), the lane it is in, its label, and the scenarios that produced it. A host file with no provenance gives
/// none; a scenario is its file, line and name, never its steps.
/// </summary>
public sealed record MachineStickyDescription(
    string Id,
    string Kind,
    string Machine,
    string Label,
    IReadOnlyList<MachineScenarioDescription> Scenarios
);

/// <summary>A scenario that produced a sticky: its feature file, its line and its name.</summary>
public sealed record MachineScenarioDescription(string File, long Line, string Scenario);

/// <summary>An arrow on the board, from one sticky's id to another's.</summary>
public sealed record MachineBoardLinkDescription(string From, string To);
