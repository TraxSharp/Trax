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
