namespace Shopfront.Trains.PlaceOrder.Models;

internal record PricedCart(List<PricedLine> Lines, decimal Total);
