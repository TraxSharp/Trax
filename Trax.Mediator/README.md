# Trax.Mediator

[![NuGet Version](https://img.shields.io/nuget/v/Trax.Mediator)](https://www.nuget.org/packages/Trax.Mediator/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Dispatch station for [Trax](https://www.nuget.org/packages/Trax.Effect/) trains — hand it the cargo and it routes it to the right train, no need to know which train handles what.

## The Problem

When one part of your system needs to send a train, it has to know exactly which train class to use. Controllers depend on concrete train types, stops that trigger other trains need direct references, and everything gets coupled together.

## With TrainBus

`TrainBus` is a dispatch station. At startup it builds a map of what cargo goes on which train. To send something, you just drop off the cargo:

```csharp
public class OrderController(ITrainBus trainBus) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(OrderRequest request)
    {
        var receipt = await trainBus.RunAsync<OrderReceipt>(request);
        return Ok(receipt);
    }
}
```

No reference to `ProcessOrderTrain`. The dispatch station looks at the cargo type and sends it on the right train.

## Installation

```bash
dotnet add package Trax.Mediator
```

Trax.Mediator depends on [Trax.Effect](https://www.nuget.org/packages/Trax.Effect/), which depends on [Trax.Core](https://www.nuget.org/packages/Trax.Core/). Both are pulled in transitively.

## Setup

Register your train assemblies during startup. The dispatch station scans them for all `IServiceTrain<TIn, TOut>` implementations and builds the cargo-to-train map automatically:

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(Program).Assembly)
);
```

Multiple assemblies:

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(
            typeof(Program).Assembly,
            typeof(SomeTrainInAnotherProject).Assembly
        )
);
```

That's it. Every `IServiceTrain<TIn, TOut>` in those assemblies is now dispatchable through `ITrainBus`.

## Usage

### Dispatching a train

```csharp
// With a return value — send cargo, get a delivery back
var user = await trainBus.RunAsync<User>(new CreateUserRequest
{
    Email = "jane@example.com",
    Name = "Jane"
});

// One-way — send cargo, no delivery expected
await trainBus.RunAsync(new SendNotificationRequest { UserId = userId });
```

### Cancellation

```csharp
var receipt = await trainBus.RunAsync<OrderReceipt>(request, cancellationToken);
```

The cancellation signal is forwarded to the train and all its stops.

### Nested trains

A stop can dispatch another train mid-journey. Pass the current `Metadata` to establish a parent-child relationship between the journeys:

```csharp
public class SendWelcomeEmailStep(ITrainBus trainBus) : Step<User, Unit>
{
    public override async Task<Unit> Run(User input)
    {
        await trainBus.RunAsync(new SendEmailRequest
        {
            To = input.Email,
            Template = "welcome"
        });

        return Unit.Default;
    }
}
```

### The departure board

The dispatch station exposes a registry of all known trains — useful for tooling and dashboards:

```csharp
public class TrainListEndpoint(ITrainRegistry registry)
{
    public IEnumerable<string> GetTrainNames()
        => registry.InputTypeToTrain.Values.Select(t => t.Name);
}
```

## How It Works

At startup, `AddMediator` scans the provided assemblies for types implementing `IServiceTrain<TIn, TOut>`. It builds a dictionary from cargo type (input) to train type and registers each train in the DI container. When you call `RunAsync<TOut>(input)`, the dispatch station looks up `input.GetType()`, resolves the matching train from DI, and sends it on its way.

## Part of Trax

Trax is a layered framework — each package builds on the one below it. Stop at whatever layer solves your problem.

```
Trax.Core              pipelines, steps, railway error propagation
└→ Trax.Effect         + execution logging, DI, pluggable storage
   └→ Trax.Mediator    ← you are here
      └→ Trax.Scheduler      + cron schedules, retries, dead-letter queues
         └→ Trax.Api             + GraphQL API for remote access
            └→ Trax.Dashboard       + Blazor monitoring UI
```

**Next layer:** When you need recurring background jobs with retries and dead-lettering, add [Trax.Scheduler](https://www.nuget.org/packages/Trax.Scheduler/).

Full documentation: [traxsharp.net/docs](https://traxsharp.net/docs)

## License

MIT

## Trademark & Brand Notice

Trax is an open-source .NET framework provided by TraxSharp. This project is an independent community effort and is not affiliated with, sponsored by, or endorsed by the Utah Transit Authority, Trax Retail, or any other entity using the "Trax" name in other industries.
