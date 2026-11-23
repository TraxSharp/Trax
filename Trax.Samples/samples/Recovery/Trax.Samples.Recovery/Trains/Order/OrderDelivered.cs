namespace Trax.Samples.Recovery.Trains.Order;

/// <summary>The run's output: the order reached its door.</summary>
public sealed record OrderDelivered(string OrderId, string Courier);
