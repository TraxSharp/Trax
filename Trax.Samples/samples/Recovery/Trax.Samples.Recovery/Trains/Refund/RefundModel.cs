using System.ComponentModel;
using Trax.Core.Decisions;
using Trax.Effect.Attributes;

namespace Trax.Samples.Recovery.Trains.Refund;

/// <summary>
/// The case the approval question is about. The customer's email is marked
/// <see cref="TraxSensitiveAttribute"/>, so the host must give decision recording a state hash key:
/// without one, a decision about a state that can hold a sensitive member records no hash and is
/// never replayed.
/// </summary>
public sealed record RefundCase
{
    public required string RunId { get; init; }
    public required string OrderId { get; init; }
    public required decimal Amount { get; init; }
    public required string Reason { get; init; }
    public required int PriorRefunds { get; init; }

    [TraxSensitive]
    public required string CustomerEmail { get; init; }
}

[Asks("What went wrong with this order?")]
public enum Problem
{
    [Description("It arrived damaged or broken.")]
    Damaged,

    [Description("It never arrived.")]
    Lost,

    [Description("It arrived as ordered, but the customer no longer wants it.")]
    Unwanted,
}

/// <summary>
/// What checking the problem found, and the state the approval question is about. Every track of the
/// switch produces one. It holds nothing sensitive, unlike <see cref="RefundCase"/>.
/// </summary>
public sealed record RefundEvidence(
    string RunId,
    string OrderId,
    decimal Amount,
    int PriorRefunds,
    string Problem,
    string Finding
);

/// <summary>
/// What happened to the refund. Every track of the gate produces one, and the checkpoint stores it: a
/// retry after it starts from this, so it is a sealed record of data.
/// </summary>
public sealed record RefundOutcome(string RunId, string OrderId, string Status, decimal Amount);

/// <summary>The train's output.</summary>
public sealed record RefundResult(
    string OrderId,
    string Status,
    decimal Amount,
    bool CustomerNotified
);

[Asks(
    "Should this refund be paid without a person reviewing it?",
    Yes = "Pay it now: the amount is modest and the customer's history is clean.",
    No = "Decline it: the request does not qualify for a refund."
)]
public sealed class ApproveRefund;
