using System.Collections.Concurrent;
using Trax.StateMachine;

namespace Trax.Samples.StateMachine.E2E.Utilities;

/// <summary>One charge: the state it was made from, the amount in cents, the items and the receipt.</summary>
public sealed record Charged(
    string State,
    long AmountCents,
    IReadOnlyList<string> Items,
    string Receipt
);
