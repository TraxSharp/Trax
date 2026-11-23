using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

/// <summary>Where a ticket is on the board. Each lane is one state; Done and Closed are the last column.</summary>
public enum TicketState
{
    Triage,
    Reproducing,
    ReadyToFix,
    InQA,
    InReview,
    Done,
    Closed,
}
