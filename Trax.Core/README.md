# Trax.Core

[![NuGet Version](https://img.shields.io/nuget/v/Trax.Core)](https://www.nuget.org/packages/Trax.Core/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Railway Oriented Programming for .NET — build trains that carry data through a sequence of stops, with automatic derailment handling when something goes wrong.

## Why?

Error handling tends to bury the actual logic:

```csharp
public async Task<OrderReceipt> ProcessOrder(OrderRequest request)
{
    var inventory = await _inventory.CheckAsync(request.Items);
    if (!inventory.Available)
        return Error("Items out of stock");

    var payment = await _payments.ChargeAsync(request.PaymentMethod, request.Total);
    if (!payment.Success)
        return Error("Payment failed");

    var shipment = await _shipping.CreateAsync(request.Address, request.Items);
    if (shipment == null)
        return Error("Shipping setup failed");

    return new OrderReceipt(payment, shipment);
}
```

Every step needs its own null check, error branch, and early return. The business logic — check inventory, charge payment, create shipment — gets lost in the noise.

## With Trax.Core

```csharp
public class ProcessOrderTrain : Train<OrderRequest, OrderReceipt>
{
    protected override async Task<Either<Exception, OrderReceipt>> RunInternal(OrderRequest input)
        => Activate(input)
            .Chain<CheckInventoryStep>()
            .Chain<ChargePaymentStep>()
            .Chain<CreateShipmentStep>()
            .Resolve();
}
```

A train departs with its cargo (`Activate`), visits each stop along the route (`.Chain<T>`), and arrives at its destination (`Resolve`). If `CheckInventoryStep` throws, the train derails — `ChargePaymentStep` and `CreateShipmentStep` are never reached. The exception propagates through the chain automatically.

```
Main Track:     Input → [Stop 1] → [Stop 2] → [Stop 3] → Output
                            ↓
Derailed:              Exception → [Skip]  → [Skip]  → Exception
```

Each stop is its own class with its own dependencies, testable in isolation.

## Installation

Requires `net10.0`.

```bash
dotnet add package Trax.Core
```

## Quick Start

**1. Define a stop.** Each step takes one type of cargo in and produces one type of cargo out:

```csharp
public class ValidateEmailStep(IUserRepository repo) : Step<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        var existing = await repo.GetByEmailAsync(input.Email);
        if (existing is not null)
            throw new ValidationException($"Email {input.Email} is already taken");

        return Unit.Default;
    }
}
```

**2. Build a route by chaining stops into a train:**

```csharp
public class CreateUserTrain : Train<CreateUserRequest, User>
{
    protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
        => Activate(input)
            .Chain<ValidateEmailStep>()
            .Chain<CreateUserInDatabaseStep>()
            .Chain<SendWelcomeEmailStep>()
            .Resolve();
}
```

`Activate` loads the initial cargo and the train departs. At each stop, `.Chain<T>` picks up the cargo `T` needs from what the train is carrying, runs the step, and loads the output back on. `Resolve` unloads the final delivery at the destination.

The train carries all of this in **Memory** — a type-keyed store that accumulates as the train moves through its route. Each stop can use anything a previous stop produced.

**3. Run it:**

```csharp
var train = new CreateUserTrain();
Either<Exception, User> result = await train.RunEither(request);

// Or throw on failure:
User user = await train.Run(request);
```

## Compile-Time Validation

Trax.Core ships with a Roslyn analyzer that validates your route at build time. If a stop expects cargo that no previous stop has loaded, you get a compiler error — not a runtime derailment.

| Diagnostic | Meaning |
|------------|---------|
| **CHAIN001** | A stop expects cargo that isn't on the train at that point in the route |
| **CHAIN002** | The train's final delivery type isn't on board when `Resolve()` is called |

## IDE Extensions

Inlay hint extensions show `TIn → TOut` types inline for each `.Chain<TStep>()` call, so you can see what cargo flows through each stop at a glance.

- **VSCode** — [Trax.Core Chain Hints](https://marketplace.visualstudio.com/items?itemName=Trax.Core.trax-hints) on the Marketplace
- **Rider / ReSharper** — Search for **Trax.Core Chain Hints** in JetBrains Marketplace

## Part of Trax

Trax is a layered framework — each package builds on the one below it. Stop at whatever layer solves your problem.

```
Trax.Core          ← you are here
└→ Trax.Effect         + execution logging, DI, pluggable storage
   └→ Trax.Mediator       + decoupled dispatch via TrainBus
      └→ Trax.Scheduler      + cron schedules, retries, dead-letter queues
         └→ Trax.Api             + GraphQL API for remote access
            └→ Trax.Dashboard       + Blazor monitoring UI
```

**Next layer:** When you need execution logging, DI, or persistent metadata, add [Trax.Effect](https://www.nuget.org/packages/Trax.Effect/).

Full documentation: [traxsharp.github.io/Trax.Docs](https://traxsharp.github.io/Trax.Docs)

## License

MIT
