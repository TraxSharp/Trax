---
title: "1.7 Run it"
description: "Chapter 1 of the Trax book, section 7: Run it."
type: tutorial
area: general
parent: "1. An order is a train"
grand_parent: The Trax book
nav_order: 7
snippet_context: none
follows: book/an-order-is-a-train/the-train
---

# 1.7 Run it

`Program.cs`:

```csharp compile=app file=Program.cs
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Trax.Core.Exceptions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Catalogue>();
builder.Services.AddSingleton<IPaymentProvider, FakePaymentProvider>();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
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

`AddMediator` finds `PlaceOrderTrain` in the assembly and registers it under `IPlaceOrderTrain`.
The junctions get the catalogue, the payment provider and their loggers from the container, like
any other class. `SaveTrainParameters` keeps each run's input and output with its record, which
chapter 2's tests read back.

The endpoint passes the request's `CancellationToken` to `Run`, which is where every junction's
token comes from, and turns a `TrainException` into a `422` with its message. Anything else is a
bug, and ASP.NET Core answers it with a `500`.

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

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)

---

Next: [1.8 When an order fails](/docs/book/an-order-is-a-train/when-an-order-fails)
