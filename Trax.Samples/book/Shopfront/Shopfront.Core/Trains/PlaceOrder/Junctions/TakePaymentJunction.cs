using Shopfront.Inventory;
using Shopfront.Machines;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class TakePaymentJunction(
    IPaymentProvider payments,
    Catalogue catalogue,
    IServiceProvider services,
    ILogger<TakePaymentJunction> logger
)
    : EffectJunction<
        (Customer Customer, PlaceOrderInput Order, PricedCart Cart, StockReservation Reservation),
        Payment
    >
{
    public override async Task<Payment> Run(
        (
            Customer Customer,
            PlaceOrderInput Order,
            PricedCart Cart,
            StockReservation Reservation
        ) input
    )
    {
        // Every try of one checkout's order carries the same key, so a retry is never charged twice.
        var idempotencyKey = InvokedRuns.Current(services) is { } checkout
            ? $"checkout:{checkout.InstanceId}"
            : null;

        ChargeResult charge;
        try
        {
            charge = await payments.Charge(
                input.Customer.Id,
                input.Order.PaymentToken,
                input.Cart.Total,
                idempotencyKey,
                CancellationToken
            );
        }
        catch (Exception ex)
        {
            // However the charge failed, the order stops here, so its stock goes back, even when
            // the run itself was cancelled.
            await catalogue.Release(
                [.. input.Cart.Lines.Select(l => (l.Sku, l.Quantity))],
                CancellationToken.None
            );
            logger.LogInformation("Released reservation {ReservationId}", input.Reservation.Id);

            if (ex is PaymentDeclinedException)
                throw new TrainException(ex.Message);
            throw;
        }

        logger.LogInformation(
            "Charged {Total} as {TransactionId}",
            input.Cart.Total,
            charge.TransactionId
        );
        return new Payment(charge.TransactionId, input.Cart.Total, charge.ReceiptUrl);
    }
}
