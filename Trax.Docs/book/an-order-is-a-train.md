---
title: "1. An order is a train"
description: "Chapter 1 of the Trax book: start Shopfront and write placing an order as a train of four junctions, run in-process with in-memory storage."
type: tutorial
area: general
nav_order: 11
snippet_context: none
---

# 1. An order is a train

In this chapter we start Shopfront and write its most important piece of work, placing an order, as
a train. By the end, `POST /orders` prices a cart, reserves stock, takes a payment and confirms the
order, and the log shows each of those steps as it happens.

Every C# block on this page is compiled in CI against the package versions in the project file below.

## Requirements

- The .NET 10 SDK. Trax packages target `net10.0` only.
- `curl`, to call the application.

## Create the project

```bash
dotnet new web -n Shopfront
cd Shopfront
```

Replace `Shopfront.csproj` with this:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Trax.Effect" Version="1.61.0" />
    <PackageReference Include="Trax.Effect.Data.InMemory" Version="1.61.0" />
    <PackageReference Include="Trax.Mediator" Version="1.61.0" />
  </ItemGroup>
</Project>
```

`Trax.Effect` holds trains and junctions, `Trax.Effect.Data.InMemory` keeps a record of every run in
memory, and `Trax.Mediator` finds our trains and hands them to whoever asks.

By the end of the chapter the project looks like this:

```text
Shopfront/
├── Program.cs
├── Inventory/
│   └── Catalogue.cs
├── Payments/
│   ├── IPaymentProvider.cs
│   └── FakePaymentProvider.cs
└── Trains/
    └── PlaceOrder/
        ├── PlaceOrderInput.cs
        ├── PlaceOrderOutput.cs
        ├── IPlaceOrderTrain.cs
        ├── PlaceOrderTrain.cs
        ├── Junctions/
        │   ├── PriceCartJunction.cs
        │   ├── ReserveStockJunction.cs
        │   ├── TakePaymentJunction.cs
        │   └── ConfirmOrderJunction.cs
        └── Models/
            ├── OrderLine.cs
            ├── PricedCart.cs
            ├── PricedLine.cs
            ├── StockReservation.cs
            └── Payment.cs
```

Each train gets a folder under `Trains/`. Its top level is the train's contract: the input, the
output, the interface and the train. Its junctions go in `Junctions/`, one file each, and every
other type it uses goes in `Models/`. The shop itself, what it sells and how it is paid, lives outside
`Trains/`, because more than one train will use it. [Organize trains in a
project](/docs/core/organize-trains) has the layout on its own.

## The shop

Before any Trax code, the shop needs something to sell and a way to be paid. Both are plain classes.

`Inventory/Catalogue.cs`:

```csharp compile=app
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
}
```

The caps are out of stock on purpose. We will try to buy one.

`Payments/IPaymentProvider.cs`:

```csharp compile=app
namespace Shopfront.Payments;

public interface IPaymentProvider
{
    Task<string> Charge(string customerId, decimal amount);
}
```

`Payments/FakePaymentProvider.cs`:

```csharp compile=app
namespace Shopfront.Payments;

// Always succeeds. Chapter 2 makes it fail.
public class FakePaymentProvider : IPaymentProvider
{
    public Task<string> Charge(string customerId, decimal amount) =>
        Task.FromResult($"tx_{Guid.NewGuid():N}"[..11]);
}
```

## What an order carries

A train takes one input and returns one output, and each junction in it does the same. Every value
that moves between junctions gets a type of its own.

`Trains/PlaceOrder/PlaceOrderInput.cs`:

```csharp compile=app
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Trains.PlaceOrder;

public record PlaceOrderInput
{
    public required string CustomerId { get; init; }
    public required List<OrderLine> Lines { get; init; }
}
```

`Trains/PlaceOrder/PlaceOrderOutput.cs`:

```csharp compile=app
namespace Shopfront.Trains.PlaceOrder;

public record PlaceOrderOutput(Guid OrderId, decimal Total, string TransactionId);
```

`PlaceOrderInput` is what the customer sends and `PlaceOrderOutput` is what they get back.
Every other type goes in `Models/`. An order line is part of the input, so callers build it, and it
is `public`:

`Trains/PlaceOrder/Models/OrderLine.cs`:

```csharp compile=app
namespace Shopfront.Trains.PlaceOrder.Models;

public record OrderLine(string Sku, int Quantity);
```

The other four are what the junctions hand each other on the way. Nothing outside the train should
depend on them, so they are `internal`, as the junctions below are:

`Trains/PlaceOrder/Models/PricedCart.cs`:

```csharp compile=app
namespace Shopfront.Trains.PlaceOrder.Models;

internal record PricedCart(List<PricedLine> Lines, decimal Total);
```

`Trains/PlaceOrder/Models/PricedLine.cs`:

```csharp compile=app
namespace Shopfront.Trains.PlaceOrder.Models;

internal record PricedLine(string Sku, int Quantity, decimal UnitPrice);
```

`Trains/PlaceOrder/Models/StockReservation.cs`:

```csharp compile=app
namespace Shopfront.Trains.PlaceOrder.Models;

internal record StockReservation(Guid Id);
```

`Trains/PlaceOrder/Models/Payment.cs`:

```csharp compile=app
namespace Shopfront.Trains.PlaceOrder.Models;

internal record Payment(string TransactionId, decimal Amount);
```

## The junctions

Placing an order takes four steps, so we write four junctions. They are `internal`: the rest of the
application only ever talks to the train.

`Trains/PlaceOrder/Junctions/PriceCartJunction.cs`:

```csharp compile=app
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class PriceCartJunction(Catalogue catalogue, ILogger<PriceCartJunction> logger)
    : EffectJunction<PlaceOrderInput, PricedCart>
{
    public override Task<PricedCart> Run(PlaceOrderInput input)
    {
        if (input.Lines.Count == 0)
            throw new ArgumentException("The cart is empty.");

        var lines = input
            .Lines.Select(line =>
            {
                if (line.Quantity < 1)
                    throw new ArgumentException($"{line.Sku}: the quantity must be at least 1.");
                var price =
                    catalogue.PriceOf(line.Sku)
                    ?? throw new ArgumentException($"{line.Sku} is not in the catalogue.");
                return new PricedLine(line.Sku, line.Quantity, price);
            })
            .ToList();

        var cart = new PricedCart(lines, lines.Sum(l => l.UnitPrice * l.Quantity));
        logger.LogInformation("Priced {Count} lines at {Total}", lines.Count, cart.Total);
        return Task.FromResult(cart);
    }
}
```

Each junction says what it takes and what it makes in its type: `PriceCartJunction` takes a
`PlaceOrderInput` and makes a `PricedCart`. A junction that throws stops the order there, and
nothing after it runs.

`Trains/PlaceOrder/Junctions/ReserveStockJunction.cs`:

```csharp compile=app
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class ReserveStockJunction(Catalogue catalogue, ILogger<ReserveStockJunction> logger)
    : EffectJunction<PricedCart, StockReservation>
{
    public override Task<StockReservation> Run(PricedCart cart)
    {
        var shortSku = catalogue.TryReserve([.. cart.Lines.Select(l => (l.Sku, l.Quantity))]);
        if (shortSku is not null)
            throw new InvalidOperationException($"{shortSku} is out of stock.");

        var reservation = new StockReservation(Guid.NewGuid());
        logger.LogInformation("Reserved stock as {ReservationId}", reservation.Id);
        return Task.FromResult(reservation);
    }
}
```

`Trains/PlaceOrder/Junctions/TakePaymentJunction.cs`:

```csharp compile=app
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class TakePaymentJunction(IPaymentProvider payments, ILogger<TakePaymentJunction> logger)
    : EffectJunction<(PlaceOrderInput Order, PricedCart Cart), Payment>
{
    public override async Task<Payment> Run((PlaceOrderInput Order, PricedCart Cart) input)
    {
        var transactionId = await payments.Charge(input.Order.CustomerId, input.Cart.Total);
        logger.LogInformation(
            "Charged {Total} as {TransactionId}",
            input.Cart.Total,
            transactionId
        );
        return new Payment(transactionId, input.Cart.Total);
    }
}
```

`TakePaymentJunction` takes a tuple. It needs the customer, which is on the input, and the total,
which is on the priced cart, so it asks for both.

`Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs`:

```csharp compile=app
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
            input.Payment.TransactionId
        );
        logger.LogInformation("Confirmed order {OrderId}", output.OrderId);
        return Task.FromResult(output);
    }
}
```

## The train

`Trains/PlaceOrder/IPlaceOrderTrain.cs`:

```csharp compile=app
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.PlaceOrder;

public interface IPlaceOrderTrain : IServiceTrain<PlaceOrderInput, PlaceOrderOutput>;
```

`Trains/PlaceOrder/PlaceOrderTrain.cs`:

```csharp compile=app
using Shopfront.Trains.PlaceOrder.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.PlaceOrder;

public class PlaceOrderTrain : ServiceTrain<PlaceOrderInput, PlaceOrderOutput>, IPlaceOrderTrain
{
    protected override Task<Either<Exception, PlaceOrderOutput>> Junctions() =>
        Chain<PriceCartJunction>()
            .Chain<ReserveStockJunction>()
            .Chain<TakePaymentJunction>()
            .Chain<ConfirmOrderJunction>()
            .Resolve();
}
```

The train is only the order of its junctions. Nothing here passes a value from one junction to the
next: the train carries a store called Memory, which holds the input when the train starts and
each junction's output as it finishes, one value per type. Every junction takes its input from
there. That is how `TakePaymentJunction` gets the `PlaceOrderInput` even though the junction just
before it made a `StockReservation`.

Nothing reads the `StockReservation` yet. It sits in Memory until a later chapter needs it to give
the stock back when a payment fails.

`IPlaceOrderTrain` is how the rest of the application asks for the train, and its full name is the
name Trax records each run under.

## Run it

`Program.cs`:

```csharp compile=app
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Catalogue>();
builder.Services.AddSingleton<IPaymentProvider, FakePaymentProvider>();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UseInMemory()).AddMediator(typeof(Program).Assembly)
);

var app = builder.Build();

app.MapPost(
    "/orders",
    async (PlaceOrderInput input, IPlaceOrderTrain train) =>
    {
        try
        {
            return Results.Ok(await train.Run(input));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(ex.Message);
        }
    }
);

app.Run();
```

`AddMediator` finds `PlaceOrderTrain` in the assembly and registers it under `IPlaceOrderTrain`.
The junctions get the catalogue, the payment provider and their loggers from the container, like
any other class.

Start it:

```bash
dotnet run --urls http://localhost:5080
```

In a second terminal, buy two mugs and a T-shirt:

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"ada","lines":[{"sku":"MUG","quantity":2},{"sku":"TEE","quantity":1}]}'
```

```json
{"orderId":"dc74b072-253f-42c8-8f3c-7e7a92110932","total":49.00,"transactionId":"tx_d95607af"}
```

The application's log shows the four junctions in order:

```text
info: Shopfront.Trains.PlaceOrder.Junctions.PriceCartJunction[0]
      Priced 2 lines at 49.00
info: Shopfront.Trains.PlaceOrder.Junctions.ReserveStockJunction[0]
      Reserved stock as 23978801-97ad-4259-b404-b7bb5176f27d
info: Shopfront.Trains.PlaceOrder.Junctions.TakePaymentJunction[0]
      Charged 49.00 as tx_d95607af
info: Shopfront.Trains.PlaceOrder.Junctions.ConfirmOrderJunction[0]
      Confirmed order dc74b072-253f-42c8-8f3c-7e7a92110932
```

Your ids will differ.

## When an order fails

Add a cap to a mug:

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"ada","lines":[{"sku":"MUG","quantity":1},{"sku":"CAP","quantity":1}]}'
```

```json
"CAP is out of stock."
```

That is a `409`. The log shows the cart was priced, then the train stopped:

```text
info: Shopfront.Trains.PlaceOrder.Junctions.PriceCartJunction[0]
      Priced 2 lines at 30.50
fail: Trax.Effect.Services.ServiceTrain.ServiceTrain[0]
      Caught Exception (System.InvalidOperationException) with Message (CAP is out of stock.).
```

`ReserveStockJunction` threw, so no payment was taken and no order was confirmed. `Run` rethrew the
exception as it was thrown, which is how `Program.cs` could turn it into a `409`. Because the
catalogue reserves all or nothing, the mug was not reserved either.

Send an empty cart and the order stops one junction earlier, at pricing, with a `400`:

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"ada","lines":[]}'
```

```json
"The cart is empty."
```

Stop the application with Ctrl+C.

## When the chain does not fit

Swap the first two junctions in `PlaceOrderTrain`, so stock is reserved before the cart is priced:

```csharp fragment
Chain<ReserveStockJunction>()
    .Chain<PriceCartJunction>()
    .Chain<TakePaymentJunction>()
    .Chain<ConfirmOrderJunction>()
    .Resolve();
```

It builds. Run it, and the application refuses to start:

```text
Unhandled exception. Trax.Core.Exceptions.TrainException: 1 of 1 registered trains cannot run:
  - IPlaceOrderTrain: step 1 (ReserveStockJunction) needs 'Shopfront.Trains.PlaceOrder.Models.PricedCart' in Memory and nothing before it puts one there. Chain a junction that produces it first.
```

When the application starts, Trax reads every train's chain and checks that each junction's input
is in Memory by the time it runs. `ReserveStockJunction` needs a `PricedCart`, and at step 1 Memory
holds only the `PlaceOrderInput`. A chain that cannot run stops the application before it takes a
single request, rather than failing the first order that reaches it.

Swap the junctions back before going on.

## What we have

Shopfront takes an order, prices it, reserves stock, charges the customer and confirms it, as four
small junctions that each do one thing. A junction that fails stops the order where it is, and a
chain in the wrong order never starts.

What we cannot do yet is look back. The runs are recorded, but in memory, and they are gone when the
application stops. In chapter 2 we move them to Postgres, make the payment provider fail now and
then, and read back what happened to each order.

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)
