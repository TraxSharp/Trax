using System.ComponentModel;
using Trax.Core.Decisions;
using Trax.Effect.Attributes;

namespace Trax.Samples.Recovery.Trains.Refund;

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
