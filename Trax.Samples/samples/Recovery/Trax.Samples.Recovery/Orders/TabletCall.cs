using System.Collections.Concurrent;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Orders;

/// <summary>One call to the restaurant's tablet: answered when <see cref="Problem"/> is null.</summary>
public sealed record TabletCall(
    string OrderId,
    int Attempt,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string? Problem
);
