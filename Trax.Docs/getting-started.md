---
title: Quick start
description: "A tutorial building one Trax app from an empty folder: a train run in-process, recorded in Postgres, scheduled, served over GraphQL, shown in the console."
type: tutorial
area: general
section: none
nav_order: 1
snippet_context: none
---

# Quick start: build a greeter service

In this tutorial we build one application from an empty folder: a train that greets someone, run
in-process first, then recorded in Postgres, scheduled, and called over GraphQL and watched from the
operations console. Each step shows every file it changes in full, and what you should see when you run it.

Every C# block on this page is compiled in CI against the package versions in the project file below.

## Requirements

- The .NET 10 SDK. Trax packages target `net10.0` only.
- Docker, from step 4 on, for Postgres.
- `curl`, to call the application.

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
  </PropertyGroup>

  <ItemGroup>
    <!-- Steps 2 and 3: trains, the effect system, the train bus. -->
    <PackageReference Include="Trax.Effect" Version="1.61.0" />
    <PackageReference Include="Trax.Effect.Data.InMemory" Version="1.61.0" />
    <PackageReference Include="Trax.Mediator" Version="1.61.0" />

    <!-- Step 4: Postgres, saved inputs and outputs, a log line per junction. -->
    <PackageReference Include="Trax.Effect.Data.Postgres" Version="1.61.0" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" Version="1.61.0" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Logging" Version="1.61.0" />

    <!-- Step 5: the scheduler. -->
    <PackageReference Include="Trax.Scheduler" Version="1.61.0" />

    <!-- Step 6: GraphQL, an API key to call it with, and the operations console. -->
    <PackageReference Include="Trax.Api.GraphQL" Version="1.61.0" />
    <PackageReference Include="Trax.Api.Auth.ApiKey" Version="1.61.0" />
    <PackageReference Include="Trax.Api.Dashboard" Version="1.61.0" />
  </ItemGroup>
</Project>
```

Every Trax package shares one version, the release this site was published from. If your project
also pins EF Core, Npgsql or a `Microsoft.Extensions.*` package itself and the restore fails with
`NU1605`, see [Packages](/docs/reference/packages).

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
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;

namespace Greeter;

public class ValidateNameJunction : EffectJunction<GreetInput, Unit>
{
    public override Task<Unit> Run(GreetInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new ArgumentException("A greeting needs a name.");

        return Task.FromResult(Unit.Default);
    }
}

public class BuildGreetingJunction(ILogger<BuildGreetingJunction> logger)
    : EffectJunction<GreetInput, Greeting>
{
    public override Task<Greeting> Run(GreetInput input)
    {
        logger.LogInformation("Greeting {Name}", input.Name);
        return Task.FromResult(new Greeting($"Hello, {input.Name}!"));
    }
}
```

Both derive `EffectJunction`, so the junction logger we add in step 4 sees them.

`GreetTrain.cs`:

```csharp compile=app,postgres,schedule
using Trax.Core.Functional;
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

Build it:

```bash
dotnet build
```

It restores the packages and reports that the build succeeded. The template's
`Program.cs` still serves "Hello World!"; we replace it next.

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

Start it on port 5080, which every step on this page uses:

```bash
dotnet run --urls http://localhost:5080
```

The log ends with `Now listening on: http://localhost:5080` and `Hosting environment: Development`.
In a second terminal, greet someone:

```bash
curl -X POST http://localhost:5080/greet -H 'Content-Type: application/json' -d '{"name":"Ada"}'
```

```json
{"message":"Hello, Ada!"}
```

Now send an empty name:

```bash
curl -X POST http://localhost:5080/greet -H 'Content-Type: application/json' -d '{"name":""}'
```

```json
"A greeting needs a name."
```

That is a `400`: `ValidateNameJunction` threw, the chain stopped, and `Run` rethrew the exception
as it was thrown. Stop the application with Ctrl+C before the next step.

## 4. Record runs in Postgres

Start a database. It listens on host port 5433, so it does not collide with a Postgres you may
already run on 5432:

```bash
docker run -d --name greeter-db -p 5433:5432 \
  -e POSTGRES_USER=trax -e POSTGRES_PASSWORD=trax123 -e POSTGRES_DB=trax postgres:17
```

`docker ps --filter name=greeter-db` lists it as `Up`.

Add the connection string to `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "TraxDatabase": "Host=localhost;Port=5433;Database=trax;Username=trax;Password=trax123"
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
log entry before and after every junction.

Start the application again with `dotnet run --urls http://localhost:5080`, send both `curl`
requests from step 3, then look at what was recorded:

```bash
docker exec greeter-db psql -U trax -d trax -c \
  "select name, train_state, input->>'name' as name_in, output->>'message' as message
   from trax.metadata where name = 'Greeter.IGreetTrain' order by id desc limit 5"
```

```text
        name         | train_state | name_in |   message
---------------------+-------------+---------+-------------
 Greeter.IGreetTrain | failed      |         |
 Greeter.IGreetTrain | completed   | Ada     | Hello, Ada!
(2 rows)
```

Each row is one run of `Greeter.IGreetTrain`. The failed one also records the junction that failed
and its exception. Stop the application before the next step.

## 5. Schedule it

`Program.cs`:

```csharp compile=schedule
using Greeter;
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

`AddScheduler` stores the schedule as a manifest named `greet-world` when the application starts,
and runs the train every minute on worker threads in this process. The manifest lives in Postgres,
so it survives a restart.

Run the application. Within a minute or two the scheduled runs appear, each greeting `World`. The
`psql` query from step 4 lists them above the two earlier runs:

```text
        name         | train_state | name_in |    message
---------------------+-------------+---------+---------------
 Greeter.IGreetTrain | completed   | World   | Hello, World!
```

Stop the application before the next step.

## 6. Call it over GraphQL

A train opts into the GraphQL schema with an attribute, and has to say who may call it.

`GreetTrain.cs`:

```csharp compile=graphql
using Trax.Core.Functional;
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
using Trax.Api.Dashboard.Extensions;
using Trax.Api.GraphQL.Extensions;
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

// Two demo keys: one holding the User role, for calling the train, and one holding Operator, for
// the operations console. A key containing "do-not-use-in-production" stops the application from
// starting outside Development.
builder.Services.AddTraxApiKeyAuth(keys =>
    keys.Add("greeter-key-do-not-use-in-production", id: "demo", "User")
        .Add("greeter-operator-key-do-not-use-in-production", id: "operator", "Operator")
);

// The operations namespace is what the console reads and changes: runs, schedules, dead letters.
// It is off until exposed, and only the Operator role may use it.
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: "Operator")
);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();

// The console's page has no sign-in in front of it here, so it is only served in Development.
// Everything it shows or changes still needs the operator key.
if (app.Environment.IsDevelopment())
    app.UseTraxApiDashboard(dashboard => dashboard.AllowAnonymousShell());

app.Run();
```

The `/greet` endpoint is gone: GraphQL replaces it. `[TraxQuery]` exposes the train as a query that
runs it and returns its output. Every exposed train has to carry `[TraxAuthorize]` or
`[TraxAllowAnonymous]`, or the application refuses to start, so an operation is never public by
accident. This one needs the `User` role, which the demo key holds.

Run the application and call the query with the key:

```bash
curl -X POST http://localhost:5080/trax/graphql \
  -H 'Content-Type: application/json' \
  -H 'X-Api-Key: greeter-key-do-not-use-in-production' \
  -d '{"query":"{ discover { greet(input: { name: \"Ada\" }) { message } } }"}'
```

```json
{"data":{"discover":{"greet":{"message":"Hello, Ada!"}}}}
```

Send the same request without the `X-Api-Key` header and the response holds an `errors` entry
instead of the greeting. `http://localhost:5080/trax/graphql` in a browser opens the Nitro GraphQL
IDE, with the schema to explore.

Open `http://localhost:5080/trax/dashboard`, the operations console, and paste the operator key,
`greeter-operator-key-do-not-use-in-production`, when it asks. **Schedules** lists the
`greet-world` manifest with its next run, and **Runs** every run so far, the failed one with its
exception. The console holds the key in memory only, so it asks again on every page load.

We now have one process that runs a train on request, records every run, runs it on a schedule,
serves it over GraphQL and shows it in the operations console.

## Next steps

- [Put a sign-in in front of the console](/docs/dashboard/add-the-console-to-a-host) before it runs
  outside Development.
- [Secure a GraphQL host](/docs/api-security) with real credentials, and expose a train as a
  mutation with [TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute).
- [Start from a project template](/docs/reference/templates): `trax-hub` scaffolds this same shape
  with a test project.
- [Run a train without the effect system](/docs/core): `Trax.Core` alone, no database, no container.
- [Trains and junctions](/docs/core/trains-and-junctions): what we built in step 2, and why.
- [Samples](/docs/samples): complete applications, including the API, scheduler and workers in
  separate processes.

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) | [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) | [AddTraxGraphQL / UseTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth)
