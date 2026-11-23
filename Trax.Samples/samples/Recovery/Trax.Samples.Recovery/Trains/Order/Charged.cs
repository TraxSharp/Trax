namespace Trax.Samples.Recovery.Trains.Order;

/// <summary>The card was charged the order's total, once, under the order's id.</summary>
public sealed record Charged(string OrderId, string ChargeId, decimal Amount);
