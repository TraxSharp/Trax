---
title: "4.5 The catalogue reads the database"
description: "Chapter 4 of the Trax book, section 5: The catalogue reads the database."
type: tutorial
area: general
parent: "4. The shop's own data"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/the-shops-own-data/the-context-and-its-interface
---

# 4.5 The catalogue reads the database

The catalogue stays where chapter 1 put it, outside `Trains/`, because more than one train will
use it. It keeps its job and changes where it does it.

`Inventory/Catalogue.cs`:

```csharp compile=app file=Inventory/Catalogue.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;

namespace Shopfront.Inventory;

// What the shop sells and how many of each it has.
public class Catalogue(IShopfrontDbContext db)
{
    // The price of each of these SKUs that the shop sells.
    public Task<Dictionary<string, decimal>> PricesOf(
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken
    ) =>
        db.Products.Where(p => skus.Contains(p.Sku))
            .ToDictionaryAsync(p => p.Sku, p => p.Price, cancellationToken);

    // Reserves every line or none of them. Returns the first SKU that is short, or null.
    public async Task<string?> TryReserve(
        IReadOnlyList<(string Sku, int Quantity)> lines,
        CancellationToken cancellationToken
    )
    {
        var skus = lines.Select(l => l.Sku).ToList();
        var products = await db
            .Products.Where(p => skus.Contains(p.Sku))
            .ToDictionaryAsync(p => p.Sku, cancellationToken);

        var shortSku = lines
            .Where(l => products[l.Sku].Stock < l.Quantity)
            .Select(l => l.Sku)
            .FirstOrDefault();
        if (shortSku is not null)
            return shortSku;

        foreach (var (sku, quantity) in lines)
            products[sku].Stock -= quantity;

        // One transaction for every line. If another order changed one of these products since
        // it was read, nothing is saved and DbUpdateConcurrencyException says so.
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    // Puts reserved stock back. Each line is one atomic increment, so it never overwrites
    // another order's change.
    public async Task Release(
        IReadOnlyList<(string Sku, int Quantity)> lines,
        CancellationToken cancellationToken
    )
    {
        foreach (var (sku, quantity) in lines)
            await db
                .Products.Where(p => p.Sku == sku)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(p => p.Stock, p => p.Stock + quantity),
                    cancellationToken
                );
    }
}
```

The catalogue now holds a data context, which lives as long as one unit of work, so it is
registered per scope rather than once. Trax runs each train in a scope of its own, so every run of
`PlaceOrder` gets its own catalogue and context.

## The junctions follow

Pricing asks the catalogue for every price at once.

`Trains/PlaceOrder/Junctions/PriceCartJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/PriceCartJunction.cs
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
```

`Trains/PlaceOrder/Junctions/ReserveStockJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/ReserveStockJunction.cs
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
```

The payment junction gives the stock back with `CancellationToken.None`, not the run's token: a
customer who gives up on the request has cancelled the order, and the stock still has to go back.

`Trains/PlaceOrder/Junctions/TakePaymentJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/TakePaymentJunction.cs
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class TakePaymentJunction(
    IPaymentProvider payments,
    Catalogue catalogue,
    ILogger<TakePaymentJunction> logger
) : EffectJunction<(PlaceOrderInput Order, PricedCart Cart, StockReservation Reservation), Payment>
{
    public override async Task<Payment> Run(
        (PlaceOrderInput Order, PricedCart Cart, StockReservation Reservation) input
    )
    {
        ChargeResult charge;
        try
        {
            charge = await payments.Charge(
                input.Order.CustomerId,
                input.Order.PaymentToken,
                input.Cart.Total,
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
```

Confirming the order now stores it. The junction asks Memory for the input as well, for the
customer.

`Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs
using Shopfront.Data;
using Shopfront.Data.Models;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class ConfirmOrderJunction(IShopfrontDbContext db, ILogger<ConfirmOrderJunction> logger)
    : EffectJunction<(PlaceOrderInput Input, PricedCart Cart, Payment Payment), PlaceOrderOutput>
{
    public override async Task<PlaceOrderOutput> Run(
        (PlaceOrderInput Input, PricedCart Cart, Payment Payment) input
    )
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = input.Input.CustomerId,
            Total = input.Cart.Total,
            TransactionId = input.Payment.TransactionId,
            PlacedAt = DateTime.UtcNow,
            Items =
            [
                .. input.Cart.Lines.Select(l => new OrderItem
                {
                    Sku = l.Sku,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                }),
            ],
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(CancellationToken);

        logger.LogInformation("Confirmed order {OrderId}", order.Id);
        return new PlaceOrderOutput(
            order.Id,
            order.Total,
            order.TransactionId,
            input.Payment.ReceiptUrl
        );
    }
}
```

The order is stored after the customer is charged, so a database that fails at exactly this
point leaves a payment with no order. Charging only once, however often an order is tried, comes
later in the book.

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [EnsureSchemaCreatedAsync](/docs/effect/domain-schema-bootstrap) | [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [4.6 Two orders, one T-shirt](/docs/book/the-shops-own-data/two-orders-one-t-shirt)
