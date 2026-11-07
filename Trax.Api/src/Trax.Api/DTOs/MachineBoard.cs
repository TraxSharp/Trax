using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>
/// The event-storming board of a machine served from features, as <c>operations.machineBoard</c> serves it: one lane
/// per machine it joins, each sticky with its flow role and the scenarios behind it (ADR <c>Trax.Docs/adr/0058</c>).
/// </summary>
/// <param name="Machine">The machine the board was asked for.</param>
/// <param name="Lanes">The machines on the board, the asked-for one first.</param>
/// <param name="Stickies">Every sticky, by lane and flow role.</param>
/// <param name="Links">Every arrow between two stickies.</param>
public record MachineBoard(
    string Machine,
    IReadOnlyList<string> Lanes,
    IReadOnlyList<MachineSticky> Stickies,
    IReadOnlyList<MachineBoardLink> Links
)
{
    /// <summary>The board <paramref name="board"/> describes.</summary>
    public static MachineBoard From(MachineBoardDescription board) =>
        new(
            board.Machine,
            board.Lanes,
            board
                .Stickies.Select(s => new MachineSticky(
                    s.Id,
                    s.Kind,
                    s.Machine,
                    s.Label,
                    s.Scenarios.Select(p => new MachineScenario(p.File, p.Line, p.Scenario))
                        .ToList()
                ))
                .ToList(),
            board.Links.Select(l => new MachineBoardLink(l.From, l.To)).ToList()
        );
}

/// <summary>
/// One sticky: its flow role (<c>actor</c>, <c>command</c>, <c>event</c>, <c>policy</c>, <c>aggregate</c> or
/// <c>view</c>), its lane, its label, and the scenarios that produced it, none when the host file has no provenance.
/// </summary>
public record MachineSticky(
    string Id,
    string Kind,
    string Machine,
    string Label,
    IReadOnlyList<MachineScenario> Scenarios
);

/// <summary>A scenario behind a sticky: its feature file, line and name, never its steps.</summary>
public record MachineScenario(string File, long Line, string Scenario);

/// <summary>An arrow from one sticky's id to another's.</summary>
public record MachineBoardLink(string From, string To);
