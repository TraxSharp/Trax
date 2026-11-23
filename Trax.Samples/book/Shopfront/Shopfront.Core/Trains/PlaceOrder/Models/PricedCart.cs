namespace Shopfront.Trains.PlaceOrder.Models;

internal sealed record PricedCart(List<PricedLine> Lines, decimal Total);
