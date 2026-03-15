# Trax.Effect

[![NuGet Version](https://img.shields.io/nuget/v/Trax.Effect)](https://www.nuget.org/packages/Trax.Effect/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Effect system for [Trax.Core](https://www.nuget.org/packages/Trax.Core/) — upgrade a bare locomotive into a full commercial train service with journey logging, station services, and dependency injection.

## What This Does

`Trax.Core` gives you `Train<TIn, TOut>`: a locomotive that carries cargo through a sequence of stops. That's enough for pure logic, but production services need to know what ran, when it departed, whether it arrived, what it was carrying, and what went wrong if it derailed.

`Trax.Effect` adds the `ServiceTrain<TIn, TOut>` base class — a full commercial train service that wraps every journey with:

- **Journey logging** — a persistent metadata record for each run (state, timing, cargo in, cargo out, derailment details)
- **Station services** — pluggable effect providers that fire during execution (data persistence, logging, parameter serialization, progress tracking)
- **DI integration** — stops are resolved from `IServiceProvider`, so you get constructor injection out of the box

## Installation

```bash
dotnet add package Trax.Effect
```

For data persistence, pick a storage depot:

```bash
# PostgreSQL (production)
dotnet add package Trax.Effect.Data.Postgres

# In-memory (testing / prototyping)
dotnet add package Trax.Effect.Data.InMemory
```

Optional station services:

```bash
dotnet add package Trax.Effect.Provider.Json        # Debug logging of train state
dotnet add package Trax.Effect.Provider.Parameter    # Serialize cargo to the journey log
```

## Setup

Register station services in your `IServiceCollection`:

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
        effects.UsePostgres(connectionString).SaveTrainParameters().AddStepLogger(serializeStepData: true).AddStepProgress()
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
    protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
        => Activate(input)
            .Chain<ValidateEmailStep>()
            .Chain<CreateUserInDatabaseStep>()
            .Chain<SendWelcomeEmailStep>()
            .Resolve();
}
```

The route syntax is identical to `Train`. The difference is what happens around it — `ServiceTrain` automatically opens a journey log when the train departs, updates it when it arrives, persists effect data at each station, and records the derailment details if any stop fails.

Steps work the same way, with full DI:

```csharp
public class CreateUserInDatabaseStep(AppDbContext db) : Step<CreateUserRequest, User>
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
| **StepLogger** | Built-in | Logs each stop's execution with optional cargo serialization |
| **StepProgress** | Built-in | Tracks per-stop progress and checks for cancellation signals |

Station services compose — enable as many as you need:

```csharp
effects
    .UsePostgres(connectionString)
    .AddJson()
    .SaveTrainParameters()
    .AddStepLogger(serializeStepData: true)
    .AddStepProgress();
```

## DI Registration Helpers

Register your trains as scoped services with proper interface mapping:

```csharp
builder.Services.AddScopedTraxRoute<ICreateUserTrain, CreateUserTrain>();
builder.Services.AddTransientTraxRoute<IProcessOrderTrain, ProcessOrderTrain>();
```

Or use `AddMediator` (from [Trax.Mediator](https://www.nuget.org/packages/Trax.Mediator/)) to auto-register all trains in an assembly.

## Part of Trax

Trax is a layered framework — each package builds on the one below it. Stop at whatever layer solves your problem.

```
Trax.Core              pipelines, steps, railway error propagation
└→ Trax.Effect         ← you are here
   └→ Trax.Mediator       + decoupled dispatch via TrainBus
      └→ Trax.Scheduler      + cron schedules, retries, dead-letter queues
         └→ Trax.Api             + GraphQL API for remote access
            └→ Trax.Dashboard       + Blazor monitoring UI
```

**Next layer:** When you need decoupled dispatch (callers don't know which train handles a request), add [Trax.Mediator](https://www.nuget.org/packages/Trax.Mediator/).

Full documentation: [traxsharp.net/docs](https://traxsharp.net/docs)

## License

MIT
