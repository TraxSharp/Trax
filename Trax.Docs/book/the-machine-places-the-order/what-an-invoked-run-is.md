---
title: "13.2 What an invoked run is"
description: "Chapter 13 of the Trax book, section 2: What an invoked run is."
type: tutorial
area: general
parent: "13. The machine places the order"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/the-machine-places-the-order/the-flow-from-placing
---

# 13.2 What an invoked run is

When a checkout enters `Placing`, the server queues a run of `PlaceOrder` with an input it builds
from the checkout's context, and remembers the run. The run goes through the queue like any queued
work, so in Shopfront the worker runs it. When it ends, the hub applies its outcome to the checkout
that started it, whether or not anyone is watching. A late result from a run the checkout has
moved on from is refused.

Three things about Shopfront have to change for that to work, and each is a rule worth knowing.

## An invoked train's output holds no secret

The run's output is written into the checkout's context, which is stored as plain JSON and served
to its owner. `[TraxSensitive]` masks a value in the copies Trax keeps of a run; it cannot mask one
the train hands on to be stored. So the host refuses to start when an invoked train's output
reaches a `[TraxSensitive]` member, and `PlaceOrderOutput` has one: the receipt link. It leaves the
output. The payment's own record still carries it, masked, and the provider sends it to the
customer, as providers do.

`Shopfront.Core/Trains/PlaceOrder/PlaceOrderOutput.cs`:

```csharp compile=app file=Shopfront.Core/Trains/PlaceOrder/PlaceOrderOutput.cs
namespace Shopfront.Trains.PlaceOrder;

public record PlaceOrderOutput(Guid OrderId, decimal Total, string TransactionId);
```

`Shopfront.Core/Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs`:

```csharp compile=app file=Shopfront.Core/Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs
using Shopfront.Data;
using Shopfront.Data.Models;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class ConfirmOrderJunction(IShopfrontDbContext db, ILogger<ConfirmOrderJunction> logger)
    : EffectJunction<(Customer Customer, PricedCart Cart, Payment Payment), PlaceOrderOutput>
{
    public override async Task<PlaceOrderOutput> Run(
        (Customer Customer, PricedCart Cart, Payment Payment) input
    )
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = input.Customer.Id,
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
        return new PlaceOrderOutput(order.Id, order.Total, order.TransactionId);
    }
}
```

## The customer is the checkout's owner

`PlaceOrder` has asked who the caller is since chapter 6. An invoked run has no caller: it runs on
the worker, where nobody is signed in, and `TraxPrincipal` cannot be resolved there. What it has is
the checkout it was invoked by, and the checkout's owner. Trax hands the run an `InvokedRun` naming
both, which can only be resolved inside a run a machine invoked.

A small helper finds it, or nothing.

`Shopfront.Core/Machines/InvokedRuns.cs`:

```csharp compile=app file=Shopfront.Core/Machines/InvokedRuns.cs
using Trax.Effect.StateMachine.Persistence;

namespace Shopfront.Machines;

public static class InvokedRuns
{
    // The machine instance that invoked the current run, or null in a run no machine invoked.
    public static InvokedRun? Current(IServiceProvider services)
    {
        try
        {
            return services.GetService<InvokedRun>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
```

The caller the shop's code sees falls back to the owner. A user key is the same qualified id a
signed-in caller has, `TraxJwt:ada`, so the order is hers either way, and the fraud check and the
query filter see her orders.

`Shopfront.Core/Auth/TraxShopfrontCaller.cs`:

```csharp compile=app file=Shopfront.Core/Auth/TraxShopfrontCaller.cs
using Shopfront.Data;
using Shopfront.Machines;
using Trax.Api.Auth;

namespace Shopfront.Auth;

// The signed-in caller, or, in a run a checkout invoked, the checkout's owner.
public sealed class TraxShopfrontCaller(TraxCaller caller, IServiceProvider services)
    : IShopfrontCaller
{
    public string? CustomerId => caller.Principal?.Id ?? InvokedRuns.Current(services)?.UserKey;
    public bool IsStaff => caller.Principal?.Roles.Contains(Roles.Staff) == true;
}
```

`IdentifyCustomerJunction` asks the shop's caller, not `TraxPrincipal`, so it works on both paths.
With neither a caller nor an owner it refuses, rather than placing an order for nobody.

`Shopfront.Core/Trains/PlaceOrder/Junctions/IdentifyCustomerJunction.cs`:

```csharp compile=app file=Shopfront.Core/Trains/PlaceOrder/Junctions/IdentifyCustomerJunction.cs
using Shopfront.Data;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class IdentifyCustomerJunction(IShopfrontCaller caller)
    : EffectJunction<PlaceOrderInput, Customer>
{
    public override Task<Customer> Run(PlaceOrderInput input) =>
        Task.FromResult(
            new Customer(
                caller.CustomerId ?? throw new TrainException("Sign in to place an order.")
            )
        );
}
```

## A retry never charges twice

A failed checkout can be retried, and each try is a new run of `PlaceOrder` from its first
junction. If a try charged the card and then failed, storing the order say, the next try would
charge it again. A payment provider prevents that with an **idempotency key**: a charge sent with a
key it has seen is answered with the first charge, not made again. The key has to stay the same
across every try of one order, so it is the checkout's id.

`Shopfront.Core/Payments/IPaymentProvider.cs`:

```csharp compile=app file=Shopfront.Core/Payments/IPaymentProvider.cs
namespace Shopfront.Payments;

public interface IPaymentProvider
{
    // A charge with an idempotency key the provider has seen is answered with that charge, not
    // made again.
    Task<ChargeResult> Charge(
        string customerId,
        string paymentToken,
        decimal amount,
        string? idempotencyKey,
        CancellationToken cancellationToken
    );
}
```

`Shopfront.Core/Payments/FakePaymentProvider.cs`:

```csharp compile=app file=Shopfront.Core/Payments/FakePaymentProvider.cs
using System.Collections.Concurrent;

namespace Shopfront.Payments;

public class FakePaymentProvider : IPaymentProvider
{
    public const string DeclinedToken = "tok_chargeDeclined";

    private readonly ConcurrentDictionary<string, ChargeResult> byKey = new();
    private int charges;

    public Task<ChargeResult> Charge(
        string customerId,
        string paymentToken,
        decimal amount,
        string? idempotencyKey,
        CancellationToken cancellationToken
    )
    {
        if (idempotencyKey is not null && byKey.TryGetValue(idempotencyKey, out var earlier))
            return Task.FromResult(earlier);

        if (paymentToken == DeclinedToken)
            throw new PaymentDeclinedException("The card was declined.");

        if (Interlocked.Increment(ref charges) % 5 == 0)
            throw new TimeoutException("The payment provider did not answer.");

        var transactionId = $"tx_{Guid.NewGuid():N}"[..11];
        var charge = new ChargeResult(
            transactionId,
            $"https://pay.example/receipts/{transactionId}?key={Guid.NewGuid():N}"
        );
        if (idempotencyKey is not null)
            byKey[idempotencyKey] = charge;
        return Task.FromResult(charge);
    }
}
```

`Shopfront.Core/Trains/PlaceOrder/Junctions/TakePaymentJunction.cs`:

```csharp compile=app file=Shopfront.Core/Trains/PlaceOrder/Junctions/TakePaymentJunction.cs
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
```

An order placed over the `placeOrder` mutation has no checkout, so it is charged without a key, as
before.

## SDK Reference

> [Invoke a train from a state](/docs/statemachine/invoking-trains) | [Invoked runs](/docs/statemachine/invoked-runs) | [Spawn a child machine](/docs/statemachine/spawning-child-machines) | [Machine](/docs/sdk-reference/statemachine-api/machine)

---

Next: [13.3 The parcel](/docs/book/the-machine-places-the-order/the-parcel)
