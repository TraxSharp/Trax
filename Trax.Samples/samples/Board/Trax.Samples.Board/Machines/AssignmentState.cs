using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Trains.Checks;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

public enum AssignmentState
{
    /// <summary>The agent holds a claimed ticket and is working on it.</summary>
    Working,

    CheckingTriage,
    CheckingRepro,
    CheckingClose,
    CheckingFix,
    CheckingQa,
    CheckingReview,

    /// <summary>The server ran the evidence and did not accept it; <c>verdict</c> says why. The agent may send again.</summary>
    Rejected,

    /// <summary>The server accepted the work and moved the ticket.</summary>
    Accepted,

    /// <summary>The server accepted that the ticket cannot be reproduced and closed it.</summary>
    Closed,
}
