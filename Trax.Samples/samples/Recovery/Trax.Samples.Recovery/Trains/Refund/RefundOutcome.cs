using System.ComponentModel;
using Trax.Core.Decisions;
using Trax.Effect.Attributes;

namespace Trax.Samples.Recovery.Trains.Refund;

/// <summary>
/// What happened to the refund. Every track of the gate produces one, and the checkpoint stores it: a
/// retry after it starts from this, so it is a sealed record of data.
/// </summary>
public sealed record RefundOutcome(string RunId, string OrderId, string Status, decimal Amount);
