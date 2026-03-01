---
layout: default
title: Home
nav_order: 1
---

# Trax.Core

[![Build Status](https://github.com/Theauxm/Trax.Core/workflows/Release%20NuGet%20Package/badge.svg?branch=main)](https://github.com/Theauxm/Trax.Core/actions)
[![Test Status](https://github.com/Theauxm/Trax.Core/workflows/Trax.Core:%20Run%20CI%2FCD%20Test%20Suite/badge.svg?branch=main)](https://github.com/Theauxm/Trax.Core/actions)

Trax.Core is a .NET library for building workflows as a chain of discrete steps.

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
public class ProcessOrderWorkflow : ServiceTrain<OrderRequest, OrderReceipt>
{
    protected override async Task<Either<Exception, OrderReceipt>> RunInternal(OrderRequest input)
        => Activate(input)
            .Chain<CheckInventoryStep>()
            .Chain<ChargePaymentStep>()
            .Chain<CreateShipmentStep>()
            .Resolve();
}
```

If `CheckInventoryStep` throws, `ChargePaymentStep` never runs. The exception propagates automatically. Each step is a separate class with its own dependencies, easy to test in isolation.

```
Success Track:  Input → [Step 1] → [Step 2] → [Step 3] → Output
                            ↓
Failure Track:          Exception → [Skip] → [Skip] → Exception
```

Remove a step or reorder the chain incorrectly, and the built-in [Analyzer](analyzer.md) tells you at compile time—before you ever run the code.

For more on how this works, see [Core Concepts](concepts.md).

## IDE Extensions

Inlay hint extensions for VSCode and Rider/ReSharper. They show `TIn → TOut` types inline for each `.Chain<TStep>()` call.

- **VSCode** — Install from the [VSCode Marketplace](https://marketplace.visualstudio.com/items?itemName=Trax.Core.trax-hints)
- **Rider / ReSharper** — Search for **Trax.Core Chain Hints** in JetBrains Marketplace

See [IDE Extensions](ide-extensions.md) for details.

## Quick Start

Trax.Core 5.x requires `net10.0`. See [Getting Started](getting-started.md) for installation and your first workflow.

## Available NuGet Packages

| Package | Description | Version |
|---------|-------------|---------|
| [Trax.Core](https://www.nuget.org/packages/Trax.Core/) | Core library for Railway Oriented Programming | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Core) |
| [Trax.Effect](https://www.nuget.org/packages/Trax.Effect/) | Effects for Trax.Core Workflows | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Effect) |
| [Trax.Dashboard](https://www.nuget.org/packages/Trax.Dashboard/) | Web dashboard for inspecting Trax.Core workflows | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Dashboard) |
| [Trax.Effect.Data](https://www.nuget.org/packages/Trax.Effect.Data/) | Data persistence abstractions for Trax.Core Effects | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Effect.Data) |
| [Trax.Effect.Data.InMemory](https://www.nuget.org/packages/Trax.Effect.Data.InMemory/) | In-memory data persistence for Trax.Core Effects | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Effect.Data.InMemory) |
| [Trax.Effect.Data.Postgres](https://www.nuget.org/packages/Trax.Effect.Data.Postgres/) | PostgreSQL data persistence for Trax.Core Effects | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Effect.Data.Postgres) |
| [Trax.Effect.Provider.Json](https://www.nuget.org/packages/Trax.Effect.Provider.Json/) | JSON serialization for Trax.Core Effects | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Effect.Provider.Json) |
| [Trax.Mediator](https://www.nuget.org/packages/Trax.Mediator/) | Mediator pattern implementation for Trax.Core Effects | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Mediator) |
| [Trax.Effect.Provider.Parameter](https://www.nuget.org/packages/Trax.Effect.Provider.Parameter/) | Parameter serialization for Trax.Core Effects | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Effect.Provider.Parameter) |
| [Trax.Scheduler](https://www.nuget.org/packages/Trax.Scheduler/) | Manifest-based job scheduling for Trax.Core | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Scheduler) |
| [Trax.Scheduler.Hangfire](https://www.nuget.org/packages/Trax.Scheduler.Hangfire/) | Hangfire integration for Trax.Core Scheduler | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Scheduler.Hangfire) |
| [Trax.Samples.Templates](https://www.nuget.org/packages/Trax.Samples.Templates/) | `dotnet new` project template for Trax.Core servers | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Samples.Templates) |

## License

Trax.Core is licensed under the MIT License.

## Acknowledgements

Without the help and guidance of Mark Keaton and Douglas Seely this project would not have been possible.
