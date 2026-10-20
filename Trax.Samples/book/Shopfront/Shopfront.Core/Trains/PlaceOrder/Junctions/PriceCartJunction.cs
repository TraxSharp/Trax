using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class PriceCartJunction(Catalogue catalogue, ILogger<PriceCartJunction> logger)
    : EffectJunction<PlaceOrderInput, PricedCart>
{
    public override async Task<PricedCart> Run(PlaceOrderInput input)
    {
        if (input.Lines.Count == 0)
            throw new TrainException("The cart is empty.");

        var prices = await catalogue.PricesOf(
            [.. input.Lines.Select(l => l.Sku)],
            CancellationToken
        );

        var lines = input
            .Lines.Select(line =>
            {
                if (line.Quantity < 1)
                    throw new TrainException($"{line.Sku}: the quantity must be at least 1.");
                if (!prices.TryGetValue(line.Sku, out var price))
                    throw new TrainException($"{line.Sku} is not in the catalogue.");
                return new PricedLine(line.Sku, line.Quantity, price);
            })
            .ToList();

        var cart = new PricedCart(lines, lines.Sum(l => l.UnitPrice * l.Quantity));
        logger.LogInformation("Priced {Count} lines at {Total}", lines.Count, cart.Total);
        return cart;
    }
}
