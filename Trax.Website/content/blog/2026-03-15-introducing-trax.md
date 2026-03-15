---
title: Introducing Trax
date: "2026-03-15"
author: Trax Team
description: Trax is a .NET framework for Railway Oriented Programming — composable pipelines with built-in error handling, effect tracking, scheduling, and monitoring.
tags:
  - announcement
  - release
---

# Introducing Trax

We're excited to announce the public launch of **Trax** — a .NET framework for building business logic as composable, type-safe pipelines.

## What is Trax?

Trax brings Railway Oriented Programming to .NET. Instead of scattering error handling across your codebase, you build **trains** — typed pipelines of steps where each step's output feeds the next. If any step fails, the rest are skipped automatically.

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

## Layered Architecture

Trax is designed so you can use only what you need:

- **Trax.Core** — just pipelines, no infrastructure
- **Trax.Effect** — add execution logging and DI
- **Trax.Mediator** — decoupled dispatch
- **Trax.Scheduler** — cron schedules, retries, dead letters
- **Trax.Dashboard** — Blazor monitoring UI

## Get Started

Install the template and scaffold a project:

```bash
dotnet new install Trax.Samples.Templates
dotnet new trax-server -n MyApp
```

Or check out the [Getting Started guide](/docs/getting-started) to learn more.

## Open Source

Trax is fully open source under the MIT License. Visit us on [GitHub](https://github.com/TraxSharp) and explore the [documentation](/docs).
