namespace Trax.Samples.Recovery.Trains.Order;

/// <summary>A courier has the order.</summary>
public sealed record PickedUp(string OrderId, string Courier);
