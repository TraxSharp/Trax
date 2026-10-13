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

/// <summary>The restaurant accepted the order.</summary>
public sealed record SentToRestaurant(string OrderId);

/// <summary>The card was charged the order's total, once, under the order's id.</summary>
public sealed record Charged(string OrderId, string ChargeId, decimal Amount);

/// <summary>The kitchen made the order.</summary>
public sealed record Cooked(string OrderId);

/// <summary>A courier has the order.</summary>
public sealed record PickedUp(string OrderId, string Courier);

/// <summary>The run's output: the order reached its door.</summary>
public sealed record OrderDelivered(string OrderId, string Courier);
