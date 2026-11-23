namespace Trax.Samples.Recovery.Trains.Order;

/// <summary>
/// The run's input, built on the server from the order's cart when it is placed: the order's own id, what is in
/// it and what it costs.
/// </summary>
public sealed record OrderInput
{
    public required string OrderId { get; init; }

    public required IReadOnlyList<string> Items { get; init; }

    public required decimal Total { get; init; }
}
