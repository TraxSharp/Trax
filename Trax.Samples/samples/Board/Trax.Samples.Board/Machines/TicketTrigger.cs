using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

public enum TicketTrigger
{
    /// <summary>A worker holds the ticket in its lane.</summary>
    Claim,

    /// <summary>The claim ends without the ticket moving: its lease ran out.</summary>
    Release,

    Triaged,
    NotReproduced,
    Reproduced,
    CannotReproduce,
    FixRejected,
    Fixed,
    QaFailed,
    QaPassed,
    SentBack,
    Approved,
}
