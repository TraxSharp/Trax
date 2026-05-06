# Trax.Core

[![Build](https://github.com/TraxSharp/Trax.Core/actions/workflows/nuget_release.yml/badge.svg)](https://github.com/TraxSharp/Trax.Core/actions/workflows/nuget_release.yml)
[![NuGet Version](https://img.shields.io/nuget/v/Trax.Core)](https://www.nuget.org/packages/Trax.Core/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Trax.Core)](https://www.nuget.org/packages/Trax.Core/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Last Commit](https://img.shields.io/github/last-commit/TraxSharp/Trax.Core)](https://github.com/TraxSharp/Trax.Core/commits/main)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Core/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Core)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs)

Railway Oriented Programming for .NET. Build trains that carry data through a sequence of stops, with automatic derailment handling when something goes wrong.

## The Trax Stack

Trax is a layered framework split across several repos. You can stop at whatever layer solves your problem. **You are here: Trax.Core.**

| Repo | Adds |
|------|------|
| **[Trax.Core](https://github.com/TraxSharp/Trax.Core)** | Pipelines, junctions, railway error propagation |
| [Trax.Effect](https://github.com/TraxSharp/Trax.Effect) | Execution logging, DI, pluggable storage |
| [Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator) | Decoupled dispatch via `TrainBus` |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler) | Cron schedules, retries, dead-letter queues |
| [Trax.Api](https://github.com/TraxSharp/Trax.Api) | GraphQL API for remote access |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard) | Blazor monitoring UI |
| [Trax.Cli](https://github.com/TraxSharp/Trax.Cli) | `trax-cli` project scaffolding tool |
| [Trax.Samples](https://github.com/TraxSharp/Trax.Samples) | Sample apps and a `dotnet new` template |

Full documentation: [traxsharp.net/docs](https://traxsharp.net/docs).

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

Every junction needs its own null check, error branch, and early return. The business logic (check inventory, charge payment, create shipment) gets lost in the noise.

## With Trax.Core

```csharp
public class ProcessOrderTrain : Train<OrderRequest, OrderReceipt>
{
    protected override Task<Either<Exception, OrderReceipt>> Junctions() =>
        Chain<CheckInventoryJunction>()
            .Chain<ChargePaymentJunction>()
            .Chain<CreateShipmentJunction>()
            .Resolve();
}
```

A train picks up its cargo, visits each stop along the route (`.Chain<T>`), and arrives at its destination (`Resolve`). If `CheckInventoryJunction` throws, the train derails and `ChargePaymentJunction` and `CreateShipmentJunction` are never reached. The exception propagates through the chain automatically.

```
Main Track:     Input → [Stop 1] → [Stop 2] → [Stop 3] → Output
                            ↓
Derailed:              Exception → [Skip]  → [Skip]  → Exception
```

Each junction is its own class with its own dependencies, testable in isolation.

## Installation

Requires `net10.0`.

```bash
dotnet add package Trax.Core
```

## Quick Start

**1. Define a junction.** Each junction takes one type of cargo in and produces one type of cargo out:

```csharp
public class ValidateEmailJunction(IUserRepository repo) : Junction<CreateUserRequest, Unit>
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

**2. Build a route by chaining junctions into a train:**

```csharp
public class CreateUserTrain : Train<CreateUserRequest, User>
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<ValidateEmailJunction>()
            .Chain<CreateUserInDatabaseJunction>()
            .Chain<SendWelcomeEmailJunction>()
            .Resolve();
}
```

When the train is run with an input, the cargo is loaded automatically. At each stop, `.Chain<T>` picks up the cargo `T` needs from what the train is carrying, runs the junction, and loads the output back on. `Resolve` unloads the final delivery at the destination.

The train carries all of this in **Memory**, a type-keyed store that accumulates as the train moves through its route. Each stop can use anything a previous stop produced.

**3. Run it:**

```csharp
var train = new CreateUserTrain();
Either<Exception, User> result = await train.RunEither(request);

// Or throw on failure:
User user = await train.Run(request);
```

## Compile-Time Validation

Trax.Core ships with a Roslyn analyzer that validates your route at build time. If a stop expects cargo that no previous stop has loaded, you get a compiler error, not a runtime derailment.

| Diagnostic | Meaning |
|------------|---------|
| **CHAIN001** | A junction expects cargo that isn't on the train at that point in the route |
| **CHAIN002** | The train's final delivery type isn't on board when `Resolve()` is called |

## IDE Extensions

Inlay hint extensions show `TIn → TOut` types inline for each `.Chain<TJunction>()` call, so you can see what cargo flows through each stop at a glance.

- **VSCode**: [Trax.Core Chain Hints](https://marketplace.visualstudio.com/items?itemName=Trax.Core.trax-hints) on the Marketplace
- **Rider / ReSharper**: Search for **Trax.Core Chain Hints** in JetBrains Marketplace

## Next Layer

When you need execution logging, DI, or persistent metadata, move up to [Trax.Effect](https://github.com/TraxSharp/Trax.Effect).

## License

MIT

## Trademark & Brand Notice

Trax is an open-source .NET framework provided by TraxSharp. This project is an independent community effort and is not affiliated with, sponsored by, or endorsed by the Utah Transit Authority, Trax Retail, or any other entity using the "Trax" name in other industries.
