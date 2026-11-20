namespace Shopfront.Trains.PlaceOrder.Models;

internal sealed record PricedLine(string Sku, int Quantity, decimal UnitPrice);
