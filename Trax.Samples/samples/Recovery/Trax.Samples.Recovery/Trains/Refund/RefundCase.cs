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
