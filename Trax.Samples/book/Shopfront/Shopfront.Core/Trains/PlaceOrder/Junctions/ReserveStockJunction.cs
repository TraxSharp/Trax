using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class ReserveStockJunction(Catalogue catalogue, ILogger<ReserveStockJunction> logger)
    : EffectJunction<PricedCart, StockReservation>
{
    public override async Task<StockReservation> Run(PricedCart cart)
    {
        var shortSku = await catalogue.TryReserve(
            [.. cart.Lines.Select(l => (l.Sku, l.Quantity))],
            CancellationToken
        );
        if (shortSku is not null)
            throw new TrainException($"{shortSku} is out of stock.");

        var reservation = new StockReservation(Guid.NewGuid());
        logger.LogInformation("Reserved stock as {ReservationId}", reservation.Id);
        return reservation;
    }
}
