using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Trains.Work;

namespace Trax.Samples.Board.Tickets;

/// <summary>A lane of the board: the state it works, the role that works it, and what a claim hands its worker.</summary>
public sealed record LaneSpec(
    string Key,
    TicketState State,
    string Role,
    string Instructions,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> Events
);
