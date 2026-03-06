---
layout: default
title: Home
nav_order: 1
---

# Trax

Composable pipelines for .NET — build business logic as a typed chain of steps where errors short-circuit automatically. Start with zero infrastructure, then add execution logging, scheduling, and a monitoring dashboard as you need them.

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

## The Fix

Trax replaces that with a **train** — a typed pipeline of steps where each step's output feeds the next. If any step fails, the rest are skipped automatically:

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

```
Main Track:     Input -> [Stop 1] -> [Stop 2] -> [Stop 3] -> Output
                            |
Derailed:              Exception -> [Skip]  -> [Skip]  -> Exception
```

A compile-time [Analyzer]({{ site.baseurl }}{% link core/analyzer.md %}) catches broken chains before you ever run the code. [IDE extensions]({{ site.baseurl }}{% link core/ide-extensions.md %}) show cargo types inline at each stop.

## Use Only What You Need

Each package is a standalone layer that builds on the one below it. **Stop at whatever layer solves your problem.**

```
dotnet add package Trax.Core            # Just pipelines — no DI, no database
dotnet add package Trax.Effect          # + execution logging, DI, pluggable storage
dotnet add package Trax.Mediator        # + decoupled dispatch (callers don't know which train runs)
dotnet add package Trax.Scheduler       # + cron schedules, retries, dead-letter queues
dotnet add package Trax.Dashboard       # + Blazor monitoring UI that mounts into your app
```

| Layer | What it adds | Good for |
|-------|-------------|----------|
| [**Core**]({{ site.baseurl }}{% link core.md %}) | Trains, steps, Memory, error propagation, analyzer | Validation pipelines, CLI tools, data transforms |
| [**Effect**]({{ site.baseurl }}{% link effect.md %}) | ServiceTrain, execution metadata, DI, effect providers | Web APIs, services needing audit trails |
| [**Mediator**]({{ site.baseurl }}{% link mediator.md %}) | TrainBus dispatch by input type | Larger apps, nested trains, decoupled callers |
| [**Scheduler**]({{ site.baseurl }}{% link scheduler.md %}) | Cron/interval scheduling, retries, dead letters | Recurring jobs, ETL, background processing |
| [**Dashboard**]({{ site.baseurl }}{% link dashboard.md %}) | Blazor Server monitoring UI | Operational visibility without custom admin pages |

Once you have the layers you need, see [Samples & Deployment]({{ site.baseurl }}{% link samples.md %}) for how to structure your project and choose a deployment topology.

Or scaffold a complete project:

```bash
dotnet new install Trax.Samples.Templates
dotnet new trax-server -n MyApp
```

## Quick Start

See [Getting Started]({{ site.baseurl }}{% link getting-started.md %}) for a walkthrough at each level — from Core-only to full stack.

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
| [Trax.Api.GraphQL](https://www.nuget.org/packages/Trax.Api.GraphQL/) | GraphQL API via HotChocolate | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Api.GraphQL) |
| [Trax.Samples.Templates](https://www.nuget.org/packages/Trax.Samples.Templates/) | `dotnet new` project template for Trax servers | ![NuGet Version](https://img.shields.io/nuget/v/Trax.Samples.Templates) |

## License

Trax is licensed under the MIT License.

## Acknowledgements

Without the help and guidance of Mark Keaton, Spencer Elkington, and Douglas Seely this project would not have been possible.
