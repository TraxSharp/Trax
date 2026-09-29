# Trax.Effect

[![Build](https://github.com/TraxSharp/Trax.Effect/actions/workflows/nuget_release.yml/badge.svg)](https://github.com/TraxSharp/Trax.Effect/actions/workflows/nuget_release.yml)
[![NuGet Version](https://img.shields.io/nuget/v/Trax.Effect)](https://www.nuget.org/packages/Trax.Effect/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Trax.Effect)](https://www.nuget.org/packages/Trax.Effect/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax.Effect/blob/main/LICENSE)
[![Last Commit](https://img.shields.io/github/last-commit/TraxSharp/Trax.Effect)](https://github.com/TraxSharp/Trax.Effect/commits/main)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Effect/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Effect)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs)

The effect layer for [Trax.Core](https://www.nuget.org/packages/Trax.Core/). A Trax.Core `Train` runs a chain of junctions; a Trax.Effect `ServiceTrain` does the same inside dependency injection and records every run: a `Metadata` row with its state, timing, input, output and failure, persisted by whichever data provider you install. Optional provider packages add parameter capture, per-junction logging and progress, cross-process event broadcasting, and a persisted state-machine engine.

Full documentation: [traxsharp.net/docs](https://traxsharp.net/docs). Start with [Getting Started](https://traxsharp.net/docs/getting-started) and [Effect Providers](https://traxsharp.net/docs/effect/effect-providers).

## The Trax Stack

Trax is a layered framework split across several repos. You can stop at whatever layer solves your problem. **You are here: Trax.Effect.**

| Repo | Adds |
|------|------|
| [Trax.Core](https://github.com/TraxSharp/Trax.Core) | Pipelines, junctions, railway error propagation |
| **[Trax.Effect](https://github.com/TraxSharp/Trax.Effect)** | Execution logging, DI, pluggable storage |
| [Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator) | Decoupled dispatch via `TrainBus` |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler) | Cron schedules, retries, dead-letter queues |
| [Trax.Api](https://github.com/TraxSharp/Trax.Api) | GraphQL API for remote access |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard) | Blazor monitoring UI |
| [Trax.Cli](https://github.com/TraxSharp/Trax.Cli) | `trax-cli` project scaffolding tool |
| [Trax.Samples](https://github.com/TraxSharp/Trax.Samples) | Sample apps and a `dotnet new` template |

## What This Does

`Trax.Core` gives you `Train<TIn, TOut>`: a locomotive that carries cargo through a sequence of stops. That's enough for pure logic, but production services need to know what ran, when it departed, whether it arrived, what it was carrying, and what went wrong if it derailed.

`Trax.Effect` adds the `ServiceTrain<TIn, TOut>` base class, a full commercial train service that wraps every journey with:

- **Journey logging**: a persistent metadata record for each run (state, timing, cargo in, cargo out, derailment details)
- **Station services**: pluggable effect providers that fire during execution (data persistence, logging, parameter serialization, progress tracking)
- **DI integration**: stops are resolved from `IServiceProvider`, so you get constructor injection out of the box

## Installation

```bash
dotnet add package Trax.Effect
```

`Trax.Effect` is the one package every setup needs. The provider packages below pair with it, and each adds an extension method to the `AddEffects` builder (the broadcaster transports add theirs to the `UseBroadcaster` builder). Install a data provider to persist runs:

```bash
dotnet add package Trax.Effect.Data.Postgres   # UsePostgres(connectionString), production
dotnet add package Trax.Effect.Data.Sqlite     # UseSqlite(connectionString), single server
dotnet add package Trax.Effect.Data.InMemory   # UseInMemory(), tests and prototyping
```

Then any of the optional providers:

| Package | Adds | What it does |
|---------|------|--------------|
| `Trax.Effect.Provider.Parameter` | `SaveTrainParameters()` | Serializes each run's input and output into its `Metadata` row |
| `Trax.Effect.Provider.Json` | `AddJson()` | Logs tracked model state as JSON, for debugging |
| `Trax.Effect.JunctionProvider.Logging` | `AddJunctionLogger(serializeJunctionData)` | Logs each junction's name and duration, optionally its input and output |
| `Trax.Effect.JunctionProvider.Progress` | `AddJunctionProgress()` | Persists the current junction on the run and checks for cancellation before each one (needs a data provider) |
| `Trax.Effect.Broadcaster.RabbitMQ` | `UseRabbitMq(connectionString)` inside `UseBroadcaster(...)` | Carries train lifecycle events between processes |
| `Trax.Effect.Broadcaster.SignalR` | `UseSignalRHub()` inside `UseBroadcaster(...)`, and `MapTraxTrainEventHub()` | Pushes train lifecycle events to browser and Blazor clients |
| `Trax.Effect.StateMachine` | | The pure snapshot state-machine engine, no dependencies |
| `Trax.Effect.StateMachine.Persistence` | `AddStateMachines(assemblies)` on the builder `AddEffects` returns | Persists state-machine drafts in Postgres, with exactly-once effects |
| `Trax.Effect.Data.Testing` | | Data-layer architecture guards for your own test suite |
| `Trax.Effect.StateMachine.Testing` | | Replays a differential corpus to prove the C# and TypeScript engines agree |

You rarely install `Trax.Effect.Data` directly: every data provider depends on it.

## Setup

Register station services in your `IServiceCollection`:

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
        effects.UsePostgres(connectionString).SaveTrainParameters().AddJunctionLogger(serializeJunctionData: true).AddJunctionProgress()
    )
);
```

For development or tests, swap Postgres for in-memory:

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
        effects.UseInMemory().AddJson()
    )
);
```

## Usage

Inherit from `ServiceTrain` instead of `Train`:

```csharp
public interface ICreateUserTrain : IServiceTrain<CreateUserRequest, User> { }

public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override Task<Either<Exception, User>> Junctions()
        => Chain<ValidateEmailJunction>()
            .Chain<CreateUserInDatabaseJunction>()
            .Chain<SendWelcomeEmailJunction>()
            .Resolve();
}
```

The chain syntax is identical to `Train`. The difference is what happens around it. `ServiceTrain` automatically opens a journey log when the train departs, updates it when it arrives, persists effect data at each station, and records the derailment details if any stop fails.

Junctions work the same way, with full DI:

```csharp
public class CreateUserInDatabaseJunction(AppDbContext db) : Junction<CreateUserRequest, User>
{
    public override async Task<User> Run(CreateUserRequest input)
    {
        var user = new User { Email = input.Email, Name = input.Name };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
```

## Journey Lifecycle

Every `ServiceTrain` journey transitions through:

```
Pending → InProgress → Completed
                     → Failed
                     → Cancelled
```

Think of it as: the train is boarding (`Pending`), in transit (`InProgress`), and then either arrives (`Completed`), derails (`Failed`), or is pulled from service (`Cancelled`). These states are persisted in the journey log and queryable through the data layer.

## Station Services

| Service | Package | What it does |
|---------|---------|-------------|
| **Postgres** | `Trax.Effect.Data.Postgres` | Persists journey logs and execution data to PostgreSQL |
| **InMemory** | `Trax.Effect.Data.InMemory` | In-memory store for tests and local dev |
| **Json** | `Trax.Effect.Provider.Json` | Logs state transitions as JSON for debugging |
| **Parameter** | `Trax.Effect.Provider.Parameter` | Serializes train cargo (inputs/outputs) into the journey log |
| **Sqlite** | `Trax.Effect.Data.Sqlite` | Persists journey logs to SQLite, for a single server |
| **JunctionLogger** | `Trax.Effect.JunctionProvider.Logging` | Logs each junction's execution with optional cargo serialization |
| **JunctionProgress** | `Trax.Effect.JunctionProvider.Progress` | Tracks per-junction progress and checks for cancellation signals |

Station services compose, so enable as many as you need:

```csharp
effects
    .UsePostgres(connectionString)
    .AddJson()
    .SaveTrainParameters()
    .AddJunctionLogger(serializeJunctionData: true)
    .AddJunctionProgress();
```

Every method is documented under [Configuration](https://traxsharp.net/docs/sdk-reference/configuration).

## DI Registration Helpers

Register your trains as scoped services with proper interface mapping:

```csharp
builder.Services.AddScopedTraxRoute<ICreateUserTrain, CreateUserTrain>();
builder.Services.AddTransientTraxRoute<IProcessOrderTrain, ProcessOrderTrain>();
```

Or use `AddMediator` (from [Trax.Mediator](https://www.nuget.org/packages/Trax.Mediator/)) to auto-register all trains in an assembly.

## Next Layer

When you need decoupled dispatch (callers don't know which train handles a request), move up to [Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator).

## License

MIT

## Trademark & Brand Notice

Trax is an open-source .NET framework provided by TraxSharp. This project is an independent community effort and is not affiliated with, sponsored by, or endorsed by the Utah Transit Authority, Trax Retail, or any other entity using the "Trax" name in other industries.
