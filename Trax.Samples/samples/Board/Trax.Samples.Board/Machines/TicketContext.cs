using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

/// <summary>A ticket, one shape in every state: the report it was filed from, its claim, and what each lane left on it.</summary>
public sealed record TicketContext
{
    [MinLength(1)]
    public string Id { get; init; } = "";

    [MinLength(1)]
    public string Report { get; init; } = "";

    /// <summary>The worker holding the ticket in its lane, or null when it is free to claim.</summary>
    public string? ClaimedBy { get; init; }

    /// <summary>When the claim was taken (ISO 8601), for its lease.</summary>
    public string? ClaimedAt { get; init; }

    public string? Area { get; init; }

    /// <summary>How many repro cases the server ran that did not show the bug.</summary>
    public int? FailedCases { get; init; }

    public decimal? ReproSubtotal { get; init; }

    public decimal? ReproCharged { get; init; }

    public decimal? FixFreeFrom { get; init; }

    public bool? FixInclusive { get; init; }

    /// <summary>The last thing a lane recorded, and who did it.</summary>
    public string? Note { get; init; }

    public string? By { get; init; }
}
