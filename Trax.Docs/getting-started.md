---
layout: default
title: Getting Started
description: "A tutorial building one Trax app from an empty folder: a train run in-process, recorded in Postgres, scheduled, shown in the dashboard and served over GraphQL."
nav_order: 2
---

# Getting Started

This page builds one application from an empty folder: a train that greets someone, run in-process
first, then recorded in Postgres, scheduled, watched from the dashboard, and exposed over GraphQL.
Each step lists every file it changes in full, so you can stop after any of them with something that
runs.

Every C# block on this page is compiled in CI against the package versions in the project file below.

## Requirements

Trax requires `net10.0`. Every project that references a Trax package must target it; there is no
`net8.0` or `net9.0` build. Steps 4 to 6 also need Docker, for Postgres.

Trax's packages depend on EF Core, Npgsql and `Microsoft.Extensions.*`, so NuGet resolves those
for you. You only need to act if your project also pins one of them directly: the pin has to be
at least the version the Trax package was built against, or the restore fails with `NU1605` (`NU1109`
under Central Package Management) naming the package. The floors as of `Trax.Effect` 1.57 are:

| Package | Minimum |
|---|---|
| `Microsoft.EntityFrameworkCore` (and `.Relational`, `.InMemory`, `.Sqlite`) | 10.0.12 |
| `Npgsql` | 10.0.3 |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.3 |
| `EFCore.NamingConventions` | 10.0.1 |
| `Microsoft.Extensions.*` | 10.0.12 |

A release can raise them; the dependency list on the package's nuget.org page is authoritative.

## 1. Create the project

```bash
dotnet new web -n Greeter
cd Greeter
```

Replace `Greeter.csproj` with this. It holds every package the page uses, with the step that needs
each one:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- The dashboard's Blazor Server script (step 5). -->
    <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
  </PropertyGroup>

  <ItemGroup>
    <!-- Steps 2 and 3: trains, the effect system, the train bus. -->
    <PackageReference Include="Trax.Effect" Version="1.57.4" />
    <PackageReference Include="Trax.Effect.Data.InMemory" Version="1.57.4" />
    <PackageReference Include="Trax.Mediator" Version="1.23.3" />

    <!-- Step 4: Postgres, saved inputs and outputs, a log line per junction. -->
    <PackageReference Include="Trax.Effect.Data.Postgres" Version="1.57.4" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" Version="1.57.4" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Logging" Version="1.57.4" />

    <!-- Step 5: the scheduler and the dashboard. -->
    <PackageReference Include="Trax.Scheduler" Version="1.34.2" />
    <PackageReference Include="Trax.Dashboard" Version="1.16.0" />

    <!-- Step 6: GraphQL, and an API key to call it with. -->
    <PackageReference Include="Trax.Api.GraphQL" Version="1.44.2" />
    <PackageReference Include="Trax.Api.Auth.ApiKey" Version="1.44.2" />
  </ItemGroup>
</Project>
```

The versions are the releases this page is compiled against. Pin exact versions as these do: a
floating `Version="1.*"` restores whatever was published last, and a Trax minor release can change
an API your code calls. Newer releases are listed on each package's nuget.org page.

## 2. Define a train

A train is a chain of junctions. Each junction takes one input and produces one output, and a
junction that throws stops the chain. The train's input and output are plain types.

`Greeting.cs`:

```csharp compile=app,postgres,schedule,graphql
using Trax.Effect.Models.Manifest;

namespace Greeter;

// IManifestProperties lets the scheduler store this input (step 5).
public record GreetInput : IManifestProperties
{
    public string Name { get; init; } = "";
}

public record Greeting(string Message);
```

`Junctions.cs`:

```csharp compile=app,postgres,schedule,graphql
using LanguageExt;
using Trax.Core.Junction;

namespace Greeter;

public class ValidateNameJunction : Junction<GreetInput, Unit>
{
    public override Task<Unit> Run(GreetInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new ArgumentException("A greeting needs a name.");

        return Task.FromResult(Unit.Default);
    }
}

public class BuildGreetingJunction(ILogger<BuildGreetingJunction> logger)
    : Junction<GreetInput, Greeting>
{
    public override Task<Greeting> Run(GreetInput input)
    {
        logger.LogInformation("Greeting {Name}", input.Name);
        return Task.FromResult(new Greeting($"Hello, {input.Name}!"));
    }
}
```

`GreetTrain.cs`:

```csharp compile=app,postgres,schedule
using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Greeter;

public interface IGreetTrain : IServiceTrain<GreetInput, Greeting>;

public class GreetTrain : ServiceTrain<GreetInput, Greeting>, IGreetTrain
{
    protected override Task<Either<Exception, Greeting>> Junctions() =>
        Chain<ValidateNameJunction>().Chain<BuildGreetingJunction>().Resolve();
}
```

`Junctions()` declares the chain and does nothing else: Trax reads it at startup to check that
every junction's input is available, and refuses to start if one is not. The interface is how the
rest of the application asks for the train; its full name (`Greeter.IGreetTrain`) is the name Trax
records runs under. Junctions take constructor dependencies from the container, as
`BuildGreetingJunction` does with its logger.

## 3. Run it

`Program.cs`:

```csharp compile=app
using Greeter;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UseInMemory()).AddMediator(typeof(Program).Assembly)
);

var app = builder.Build();

app.MapPost(
    "/greet",
    async (GreetInput input, IGreetTrain train) =>
    {
        try
        {
            return Results.Ok(await train.Run(input));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }
);

app.Run();
```

`AddMediator` scans the assembly and registers every train it finds under its interface, so
`IGreetTrain` can be injected anywhere. `UseInMemory()` keeps the record of each run in memory,
which is enough to run without a database.

```bash
dotnet run
curl -X POST http://localhost:5000/greet -H 'Content-Type: application/json' -d '{"name":"Ada"}'
```

Use the port `dotnet run` prints; the examples on this page use 5000. The response is `{"message":"Hello, Ada!"}`; an empty name returns
`400` with the junction's message: `Run` throws the exception that stopped the chain, as it was
thrown. The run is recorded either way. See [Run / RunEither](/docs/sdk-reference/train-methods/run).

## 4. Record runs in Postgres

Start a database:

```bash
docker run -d --name greeter-db -p 5432:5432 \
  -e POSTGRES_USER=trax -e POSTGRES_PASSWORD=trax123 -e POSTGRES_DB=trax postgres:17
```

Add the connection string to `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "TraxDatabase": "Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123"
  }
}
```

`Program.cs`:

```csharp compile=postgres
using Greeter;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Set ConnectionStrings:TraxDatabase.");

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects.UsePostgres(connectionString).SaveTrainParameters().AddJunctionLogger()
        )
        .AddMediator(typeof(Program).Assembly)
);

var app = builder.Build();

app.MapPost(
    "/greet",
    async (GreetInput input, IGreetTrain train) =>
    {
        try
        {
            return Results.Ok(await train.Run(input));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }
);

app.Run();
```

`UsePostgres` creates and migrates the `trax` schema when the application starts.
`SaveTrainParameters()` stores each run's input and output as JSON, and `AddJunctionLogger()` writes a
log entry before and after every junction, at `Debug` unless you change it with
[SetEffectLogLevel](/docs/sdk-reference/configuration/set-effect-log-level). Run the same `curl`
again, then look at what was recorded:

```bash
docker exec greeter-db psql -U trax -d trax -c \
  "select name, train_state, input, output from trax.metadata order by id desc limit 5"
```

Each row is one run, named `Greeter.IGreetTrain`, with its state, input and output. A failed run
also records the junction that failed and its exception. [Metadata](/docs/effect/metadata) lists the
columns.

## 5. Schedule it, and open the dashboard

`Program.cs`:

```csharp compile=schedule
using Greeter;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Set ConnectionStrings:TraxDatabase.");

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects.UsePostgres(connectionString).SaveTrainParameters().AddJunctionLogger()
        )
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler.Schedule<IGreetTrain>(
                "greet-world",
                new GreetInput { Name = "World" },
                Every.Minutes(1)
            )
        )
);

// The dashboard can queue, run and cancel trains, so it refuses to start until you say who may
// use it. Open in Development; outside it, only callers who satisfy the TraxAdmin policy.
builder.AddTraxDashboard(dashboard =>
{
    if (builder.Environment.IsDevelopment())
        dashboard.AllowAnonymousDashboard();
    else
        dashboard.RequirePolicy("TraxAdmin");
});
builder.Services.AddAuthorization(options =>
    options.AddPolicy("TraxAdmin", policy => policy.RequireRole("Admin"))
);

var app = builder.Build();

app.UseAuthorization();
app.UseTraxDashboard();

app.MapPost(
    "/greet",
    async (GreetInput input, IGreetTrain train) =>
    {
        try
        {
            return Results.Ok(await train.Run(input));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }
);

app.Run();
```

`AddScheduler` stores the schedule as a manifest named `greet-world` when the application starts,
and runs the train every minute on worker threads in this process. The manifest lives in Postgres,
so it survives a restart, and changes made to it from the dashboard are kept.

`dotnet run` starts in Development (from `Properties/launchSettings.json`), so the dashboard at
`http://localhost:5000/trax` is open to you. It shows the runs as they happen, the manifest and its
next run time, and lets you run it now or disable it. Outside Development it asks for the
`TraxAdmin` policy, an authenticated user with the `Admin` role, and this application cannot sign
anyone in yet, so there the dashboard refuses every request until you add the authentication your
application uses. See [Dashboard](/docs/dashboard) for the other ways to choose who may use it.

## 6. Call it over GraphQL

A train opts into the GraphQL schema with an attribute, and has to say who may call it.

`GreetTrain.cs`:

```csharp compile=graphql
using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Greeter;

public interface IGreetTrain : IServiceTrain<GreetInput, Greeting>;

[TraxAuthorize(Roles = "User")]
[TraxQuery(Description = "Greets someone by name")]
public class GreetTrain : ServiceTrain<GreetInput, Greeting>, IGreetTrain
{
    protected override Task<Either<Exception, Greeting>> Junctions() =>
        Chain<ValidateNameJunction>().Chain<BuildGreetingJunction>().Resolve();
}
```

`Program.cs`:

```csharp compile=graphql
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;
using Greeter;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Set ConnectionStrings:TraxDatabase.");

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects.UsePostgres(connectionString).SaveTrainParameters().AddJunctionLogger()
        )
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler.Schedule<IGreetTrain>(
                "greet-world",
                new GreetInput { Name = "World" },
                Every.Minutes(1)
            )
        )
);

// A demo key, for Development only: a key containing "do-not-use-in-production" stops the
// application from starting in any other environment.
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add("greeter-key-do-not-use-in-production", id: "demo", "User")
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization(options =>
    options.AddPolicy("TraxAdmin", policy => policy.RequireRole("Admin"))
);

builder.AddTraxDashboard(dashboard =>
{
    if (builder.Environment.IsDevelopment())
        dashboard.AllowAnonymousDashboard();
    else
        dashboard.RequirePolicy("TraxAdmin");
});

builder.Services.AddTraxGraphQL();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxDashboard();
app.UseTraxGraphQL();

app.Run();
```

The `/greet` endpoint is gone: GraphQL replaces it. `[TraxQuery]` exposes the train as a query that
runs it and returns its output; a train that changes something takes `[TraxMutation]` instead, which
can also queue it for the scheduler. The schema needs at least one query, so a host that exposes
only mutations refuses to start. Every exposed train has to carry `[TraxAuthorize]` or
`[TraxAllowAnonymous]`, or the application refuses to start, so an operation is never public by
accident. This one needs the `User` role, which the demo key holds.

```bash
curl -X POST http://localhost:5000/trax/graphql \
  -H 'Content-Type: application/json' \
  -H 'X-Api-Key: greeter-key-do-not-use-in-production' \
  -d '{"query":"{ discover { greet(input: { name: \"Ada\" }) { message } } }"}'
```

In Development, `http://localhost:5000/trax/graphql` in a browser opens the Nitro GraphQL IDE, with
the schema to explore. Outside Development the IDE, the schema download and introspection are off
unless you turn them on with `AllowIntrospection`. There is no key there either, so every
operation is refused until you register real credentials: see [API Security](/docs/api-security).

## Where to go next

The `trax-hub` project template scaffolds this same shape (scheduler, dashboard and GraphQL in one
process) with an in-memory provider: `dotnet new install Trax.Samples.Templates`, then
`dotnet new trax-hub -n MyApp`. See [Project Templates](/docs/reference/templates).

- [Core](/docs/core): junctions, Memory, and the chain methods
- [Effect](/docs/effect): metadata, effect providers, and the `ServiceTrain` lifecycle
- [Mediator](/docs/mediator): running trains by input type with `ITrainBus`
- [Scheduling](/docs/scheduler): cron schedules, retries, dead letters, dependent jobs
- [Dashboard](/docs/dashboard): what each page shows, and authorization
- [API](/docs/api): queries, queued mutations, subscriptions
- [Samples & Deployment](/docs/samples): splitting the API, scheduler and workers into separate processes

Without the effect system, `Trax.Core` alone runs a `Train` you construct with `new`: no database,
no container. [Core](/docs/core) shows that form.

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect) | [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) | [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard) | [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard) | [AddTraxGraphQL / UseTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth)
