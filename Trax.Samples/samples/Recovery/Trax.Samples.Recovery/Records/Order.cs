using System.Collections.Concurrent;

namespace Trax.Samples.Recovery.Records;

/// <summary>An order as the order system holds it.</summary>
public sealed record Order(
    string OrderId,
    decimal Amount,
    string Reason,
    int PriorRefunds,
    string CustomerEmail
);
