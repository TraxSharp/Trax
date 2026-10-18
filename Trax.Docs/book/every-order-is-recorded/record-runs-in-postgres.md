---
title: "3.3 Record runs in Postgres"
description: "Chapter 3 of the Trax book, section 3: Record runs in Postgres."
type: tutorial
area: general
parent: "3. Every order is recorded"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/every-order-is-recorded/packages
---

# 3.3 Record runs in Postgres

`Program.cs`:

```csharp compile=app file=Program.cs
using Shopfront.Failures;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.FailureClassifier;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Shopfront")
    ?? throw new InvalidOperationException("ConnectionStrings:Shopfront is not set.");

builder.Services.AddSingleton<Catalogue>();
builder.Services.AddSingleton<IPaymentProvider, FakePaymentProvider>();
builder.Services.AddSingleton<IFailureClassifier, ShopfrontFailureClassifier>();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
        )
        .AddMediator(typeof(Program).Assembly)
);

var app = builder.Build();

app.MapPost(
    "/orders",
    async (PlaceOrderInput input, IPlaceOrderTrain train, CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await train.Run(input, cancellationToken));
        }
        catch (TrainException ex)
        {
            return Results.UnprocessableEntity(ex.Message);
        }
    }
);

app.Run();
```

`UsePostgres` replaces `UseInMemory`. When the application starts, it creates the `trax` schema in
the database and brings it up to date, before the host is built, so the database has to be
reachable by then. Everything after it in `AddEffects` writes through it:

- `SaveTrainParameters` stores each run's input and output with its row, as it did in memory.
- `AddJunctionLogger` logs an entry for each junction as it finishes: its name, its timing, whether
  it succeeded, and, with `serializeJunctionData: true`, what it returned.

The junction logger writes at `Debug`, which the default configuration hides. The last line under
`Logging` in `appsettings.json` shows it, by its category name.

The data provider comes first in `AddEffects`, because the effects after it write through it.
[Registration order](/docs/reference/registration-order) lists what fails when that order is
wrong.

Run it and place an order as in chapter 1. The run is a row now. Read it with `psql` inside the
container:

```bash
docker exec shopfront-postgres psql -U postgres -d shopfront \
  -c "select id, name, train_state, start_time from trax.metadata order by id"
```

Stop the application, start it again, and the row is still there.

## The provider fails

A real payment provider declines cards and sometimes does not answer. Ours will do both. The
customer pays with a payment token, the kind a provider's card form hands the browser: a reference
to the card that only the provider can use, so Shopfront never sees the card itself.

`Trains/PlaceOrder/PlaceOrderInput.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderInput.cs
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Trains.PlaceOrder;

public record PlaceOrderInput
{
    public required string CustomerId { get; init; }
    public required List<OrderLine> Lines { get; init; }
    public required string PaymentToken { get; init; }
}
```

The provider answers a charge with a transaction id and a receipt link. Anyone holding the link
can open the receipt.

`Payments/ChargeResult.cs`:

```csharp compile=app file=Payments/ChargeResult.cs
namespace Shopfront.Payments;

public record ChargeResult(string TransactionId, string ReceiptUrl);
```

`Payments/PaymentDeclinedException.cs`:

```csharp compile=app file=Payments/PaymentDeclinedException.cs
namespace Shopfront.Payments;

public class PaymentDeclinedException(string message) : Exception(message);
```

`Payments/IPaymentProvider.cs`:

```csharp compile=app file=Payments/IPaymentProvider.cs
namespace Shopfront.Payments;

public interface IPaymentProvider
{
    Task<ChargeResult> Charge(
        string customerId,
        string paymentToken,
        decimal amount,
        CancellationToken cancellationToken
    );
}
```

The fake declines one test token, as real providers' test modes do, and times out on every fifth
charge.

`Payments/FakePaymentProvider.cs`:

```csharp compile=app file=Payments/FakePaymentProvider.cs
namespace Shopfront.Payments;

public class FakePaymentProvider : IPaymentProvider
{
    public const string DeclinedToken = "tok_chargeDeclined";

    private int charges;

    public Task<ChargeResult> Charge(
        string customerId,
        string paymentToken,
        decimal amount,
        CancellationToken cancellationToken
    )
    {
        if (paymentToken == DeclinedToken)
            throw new PaymentDeclinedException("The card was declined.");

        if (Interlocked.Increment(ref charges) % 5 == 0)
            throw new TimeoutException("The payment provider did not answer.");

        var transactionId = $"tx_{Guid.NewGuid():N}"[..11];
        return Task.FromResult(
            new ChargeResult(
                transactionId,
                $"https://pay.example/receipts/{transactionId}?key={Guid.NewGuid():N}"
            )
        );
    }
}
```

## Give the stock back

Stock is reserved before the payment is taken. When the payment fails, the order stops, and the
stock it reserved has to go back on the shelf. The catalogue learns to release a reservation.

`Inventory/Catalogue.cs`:

```csharp compile=app file=Inventory/Catalogue.cs
namespace Shopfront.Inventory;

// The catalogue and its stock, in memory for now.
public class Catalogue
{
    private readonly Dictionary<string, (decimal Price, int Stock)> items = new()
    {
        ["MUG"] = (12.00m, 10),
        ["TEE"] = (25.00m, 3),
        ["CAP"] = (18.50m, 0),
    };

    public decimal? PriceOf(string sku) => items.TryGetValue(sku, out var item) ? item.Price : null;

    // Reserves every line or none of them. Returns the first SKU that is short, or null.
    public string? TryReserve(IReadOnlyList<(string Sku, int Quantity)> lines)
    {
        lock (items)
        {
            var shortSku = lines
                .Where(l => items[l.Sku].Stock < l.Quantity)
                .Select(l => l.Sku)
                .FirstOrDefault();
            if (shortSku is not null)
                return shortSku;

            foreach (var (sku, quantity) in lines)
                items[sku] = items[sku] with { Stock = items[sku].Stock - quantity };
            return null;
        }
    }

    // Puts reserved stock back.
    public void Release(IReadOnlyList<(string Sku, int Quantity)> lines)
    {
        lock (items)
        {
            foreach (var (sku, quantity) in lines)
                items[sku] = items[sku] with { Stock = items[sku].Stock + quantity };
        }
    }
}
```

The payment junction is the one that knows the payment failed, so it gives the stock back. It
asks Memory for the reservation as well, which is the `StockReservation` that has sat there unread
since chapter 1.

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
            // However the charge failed, the order stops here, so its stock goes back.
            catalogue.Release([.. input.Cart.Lines.Select(l => (l.Sku, l.Quantity))]);
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

A declined card is the customer's to fix, so its message goes back to them as a `TrainException`.
A timeout is not, and its message names our provider, so it is rethrown as it is: the endpoint
turns it into a `500`, and the run records what really happened.

## Keep the receipt link out of the record

The receipt link travels on through the train to the customer. Trax would write it into every copy
it keeps: the run's stored output, and the junction logger's entry for `TakePaymentJunction`.
`[TraxSensitive]` on a member masks it in each of those copies, while the train itself runs with
the real value.

`Trains/PlaceOrder/Models/Payment.cs`:

```csharp compile=app file=Trains/PlaceOrder/Models/Payment.cs
using Trax.Effect.Attributes;

namespace Shopfront.Trains.PlaceOrder.Models;

internal record Payment(string TransactionId, decimal Amount, [TraxSensitive] string ReceiptUrl);
```

`Trains/PlaceOrder/PlaceOrderOutput.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderOutput.cs
using Trax.Effect.Attributes;

namespace Shopfront.Trains.PlaceOrder;

public record PlaceOrderOutput(
    Guid OrderId,
    decimal Total,
    string TransactionId,
    [TraxSensitive] string ReceiptUrl
);
```

`Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class ConfirmOrderJunction(ILogger<ConfirmOrderJunction> logger)
    : EffectJunction<(PricedCart Cart, Payment Payment), PlaceOrderOutput>
{
    public override Task<PlaceOrderOutput> Run((PricedCart Cart, Payment Payment) input)
    {
        var output = new PlaceOrderOutput(
            Guid.NewGuid(),
            input.Cart.Total,
            input.Payment.TransactionId,
            input.Payment.ReceiptUrl
        );
        logger.LogInformation("Confirmed order {OrderId}", output.OrderId);
        return Task.FromResult(output);
    }
}
```

The payment token on the input is left unmarked. It is a reference only the provider can use, and
an input that carries a reference rather than the secret itself is what keeps the secret out of
every copy, including the ones `[TraxSensitive]` does not reach.

## Say whether trying again could help

A failed run records the junction and the exception. What it cannot tell from those alone is
whether running the same order again could work. A declined card will be declined again; a
provider that timed out may well answer next time. Trax records that as a failure class, and asks
one `IFailureClassifier` the application registers.

`Failures/ShopfrontFailureClassifier.cs`:

```csharp compile=app file=Failures/ShopfrontFailureClassifier.cs
using Trax.Core.Exceptions;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Failures;

public class ShopfrontFailureClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception switch
        {
            // A message for the customer: the same order fails the same way however often it runs.
            TrainException => FailureClass.Permanent,
            // The provider did not answer this time; it may next time.
            TimeoutException => FailureClass.Transient,
            _ => null,
        };
}
```

`Program.cs` above already registers it. The class is only recorded: nothing in Trax retries or
gives up because of it. It turns "which failures were worth retrying" into a query, and it is what
an operator reads first on a failed run.

## SDK Reference

> [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) | [TraxSensitive](/docs/sdk-reference/attributes/trax-sensitive) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Metadata](/docs/effect/metadata)

---

Next: [3.4 Watch it fail](/docs/book/every-order-is-recorded/watch-it-fail)
