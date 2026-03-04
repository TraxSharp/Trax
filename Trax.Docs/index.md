---
layout: default
title: Home
nav_order: 1
---

# Trax.Core

[![Build Status](https://github.com/Theauxm/Trax.Core/trains/Release%20NuGet%20Package/badge.svg?branch=main)](https://github.com/Theauxm/Trax.Core/actions)
[![Test Status](https://github.com/Theauxm/Trax.Core/trains/Trax.Core:%20Run%20CI%2FCD%20Test%20Suite/badge.svg?branch=main)](https://github.com/Theauxm/Trax.Core/actions)

Trax.Core is a .NET library for building trains — sequences of typed stops that carry data along a route with automatic derailment handling.

## The Problem

Error handling piles up fast:

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

The actual business logic gets buried under null checks and error handling.

## With Trax.Core

The same flow, without the noise:

```csharp
public class ProcessOrderTrain : ServiceTrain<OrderRequest, OrderReceipt>
{
    protected override async Task<Either<Exception, OrderReceipt>> RunInternal(OrderRequest input)
        => Activate(input)
            .Chain<CheckInventoryStep>()
            .Chain<ChargePaymentStep>()
            .Chain<CreateShipmentStep>()
            .Resolve();
}
```

The train departs with its cargo (`Activate`), visits each stop (`.Chain<T>`), and arrives at its destination (`Resolve`). If `CheckInventoryStep` throws, the train derails — `ChargePaymentStep` and `CreateShipmentStep` are never reached. Each stop is a separate class with its own dependencies, testable in isolation.

```
Main Track:     Input → [Stop 1] → [Stop 2] → [Stop 3] → Output
                            ↓
Derailed:              Exception → [Skip]  → [Skip]  → Exception
```

Remove a stop or reorder the route incorrectly, and the built-in [Analyzer](analyzer.md) tells you at compile time — before the train ever leaves the station.

For more on how this works, see [Core Concepts](concepts.md).

## IDE Extensions

Inlay hint extensions for VSCode and Rider/ReSharper. They show `TIn → TOut` cargo types inline for each `.Chain<TStep>()` stop.

- **VSCode** — Install from the [VSCode Marketplace](https://marketplace.visualstudio.com/items?itemName=Trax.Core.trax-hints)
- **Rider / ReSharper** — Search for **Trax.Core Chain Hints** in JetBrains Marketplace

See [IDE Extensions](ide-extensions.md) for details.

## Quick Start

Trax requires `net10.0`. See [Getting Started](getting-started.md) for installation and your first train.

## Available NuGet Packages

| Package | Description | Version |
|---------|-------------|---------|
| [Trax.Core](https://www.nuget.org/packages/Trax.Core/) | The locomotive — trains, stops, and railway programming | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Core) |
| [Trax.Effect](https://www.nuget.org/packages/Trax.Effect/) | Full commercial service — journey logging and station services | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Effect) |
| [Trax.Dashboard](https://www.nuget.org/packages/Trax.Dashboard/) | Operations control room for the train network | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Dashboard) |
| [Trax.Effect.Data](https://www.nuget.org/packages/Trax.Effect.Data/) | Data persistence abstractions for station services | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Effect.Data) |
| [Trax.Effect.Data.InMemory](https://www.nuget.org/packages/Trax.Effect.Data.InMemory/) | In-memory depot for testing | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Effect.Data.InMemory) |
| [Trax.Effect.Data.Postgres](https://www.nuget.org/packages/Trax.Effect.Data.Postgres/) | PostgreSQL depot for production | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Effect.Data.Postgres) |
| [Trax.Effect.Provider.Json](https://www.nuget.org/packages/Trax.Effect.Provider.Json/) | JSON logging station service | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Effect.Provider.Json) |
| [Trax.Mediator](https://www.nuget.org/packages/Trax.Mediator/) | Dispatch station — route cargo to the right train | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Mediator) |
| [Trax.Effect.Provider.Parameter](https://www.nuget.org/packages/Trax.Effect.Provider.Parameter/) | Cargo serialization station service | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Effect.Provider.Parameter) |
| [Trax.Scheduler](https://www.nuget.org/packages/Trax.Scheduler/) | Timetable management — manifests, retries, dead letters | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Scheduler) |
| [Trax.Scheduler.Hangfire](https://www.nuget.org/packages/Trax.Scheduler.Hangfire/) | Hangfire integration for the timetable | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Scheduler.Hangfire) |
| [Trax.Api](https://www.nuget.org/packages/Trax.Api/) | API core — train catalog, health checks, shared DTOs | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Api) |
| [Trax.Api.Rest](https://www.nuget.org/packages/Trax.Api.Rest/) | REST API endpoints via ASP.NET Core minimal APIs | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Api.Rest) |
| [Trax.Api.GraphQL](https://www.nuget.org/packages/Trax.Api.GraphQL/) | GraphQL API via HotChocolate | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Api.GraphQL) |
| [Trax.Samples.Templates](https://www.nuget.org/packages/Trax.Samples.Templates/) | `dotnet new` project template for Trax servers | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Samples.Templates) |

## License

Trax.Core is licensed under the MIT License.

## Acknowledgements

Without the help and guidance of Mark Keaton and Douglas Seely this project would not have been possible.
