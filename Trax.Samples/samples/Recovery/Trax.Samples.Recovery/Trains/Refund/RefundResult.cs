using System.ComponentModel;
using Trax.Core.Decisions;
using Trax.Effect.Attributes;

namespace Trax.Samples.Recovery.Trains.Refund;

/// <summary>The train's output.</summary>
public sealed record RefundResult(
    string OrderId,
    string Status,
    decimal Amount,
    bool CustomerNotified
);
