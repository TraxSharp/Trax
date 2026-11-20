using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Trains.Checks;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

/// <summary>
/// One agent's work on one ticket's lane: the ticket, the evidence it last sent, and the server's verdict on it.
/// </summary>
public sealed record AssignmentContext
{
    [MinLength(1)]
    public string TicketId { get; init; } = "";

    public string? Area { get; init; }

    public decimal? Subtotal { get; init; }

    public decimal? ExpectedShipping { get; init; }

    public decimal? FreeFrom { get; init; }

    public bool? Inclusive { get; init; }

    public List<decimal>? Subtotals { get; init; }

    public string? Decision { get; init; }

    public string? Reason { get; init; }

    /// <summary>What the server's check found, written by the check's outcome only.</summary>
    public string? Verdict { get; init; }

    /// <summary>How many repro cases on the ticket did not show the bug, as the server counted them.</summary>
    public int? FailedCases { get; init; }
}
