---
layout: default
title: Project Templates
description: "The trax-api, trax-scheduler and trax-hub dotnet new templates: what each generates, running it, its tests, adding trains, and what to change before deploying."
parent: Reference
nav_order: 4
---

# Project Templates

Trax ships three `dotnet new` templates in the `Trax.Samples.Templates` package. Each one runs
with `dotnet run` and nothing else: they use the in-memory data provider, so no database is needed
until you choose one. Each also ships a README, a `.gitignore` and an NUnit test project.

| Template | Short name | What it is | URL in Development |
|----------|------------|------------|------|
| Trax GraphQL API | `trax-api` | A GraphQL API with a typed query train and a mutation train | `http://localhost:5402` |
| Trax Scheduler with Dashboard | `trax-scheduler` | A scheduler running one train every 20 seconds, with the Trax Dashboard | `http://localhost:5401` |
| Trax Hub | `trax-hub` | The API, the scheduler and the dashboard in one process | `http://localhost:5400` |

`trax-hub` is the one to start from when you are building a Trax server and are not sure which
you need, and the one [`trax generate`](/docs/reference/cli) scaffolds.

## Installation

```bash
dotnet new install Trax.Samples.Templates
```

## Creating Projects

```bash
dotnet new trax-api --name MyCompany.Api
dotnet new trax-scheduler --name MyCompany.Scheduler
dotnet new trax-hub --name MyCompany.Hub
```

Each command creates a directory with the namespaces, file names and `.csproj` set to your
project name. The templates take no other parameters.

Package versions live in the generated `Directory.Packages.props` (Central Package Management),
one line per package, so the `.csproj` files carry no versions. They are the versions the template
was built and tested against when the package was released. To add a package, run
`dotnet add package <Name> --version <version>`, which writes the version into
`Directory.Packages.props` for you. Every Trax package releases at one version, and the template pins every `Trax.*` package at the
version it was released with. Keep them all at one version: a new `Trax.Effect.Data.Postgres` goes
in at the version `Trax.Effect` already has.

## What You Get

### trax-hub

```
MyCompany.Hub/
├── MyCompany.Hub.csproj
├── Directory.Packages.props
├── Program.cs
├── README.md
├── .gitignore
├── appsettings.json
├── Properties/
│   └── launchSettings.json
├── Auth/
│   └── DemoKeys.cs
├── Data/
│   ├── AppDbContext.cs
│   ├── AppSchema.cs
│   ├── IAppDbContext.cs
│   └── Models/
│       └── Note.cs
├── Trains/
│   ├── HelloWorld/
│   │   ├── IHelloWorldTrain.cs
│   │   ├── HelloWorldTrain.cs
│   │   ├── HelloWorldInput.cs
│   │   └── Junctions/
│   │       └── LogGreetingJunction.cs
│   └── Lookup/
│       ├── ILookupTrain.cs
│       ├── LookupTrain.cs
│       ├── LookupInput.cs
│       ├── LookupOutput.cs
│       └── Junctions/
│           └── FetchDataJunction.cs
└── tests/
    └── MyCompany.Hub.Tests/
        ├── MyCompany.Hub.Tests.csproj
        ├── UnitTests/
        │   └── FetchDataJunctionTests.cs
        └── IntegrationTests/
            ├── HelloWorldTrainTests.cs
            └── HostTests.cs
```

`Program.cs` registers, in this order, each call commented with what it does and the startup
error its removal causes:

| Registration | What it does | Without it |
|---|---|---|
| `AddTraxApiKeyAuth(...)`, Development only | The demo key `demo-key-do-not-use-in-production`, holding the `User` role | Every operation is refused, as it is in Production |
| `AddAuthentication()`, `AddAuthorization()` | ASP.NET Core authentication and authorization | Outside Development, `UseAuthentication()` throws at startup: `Unable to resolve service for type 'IAuthenticationSchemeProvider'` (`AddTraxApiKeyAuth` registers it in Development) |
| `AddTrax(...)` with `UseInMemory().SaveTrainParameters().AddJunctionProgress()` | Records every run in memory, with its input and output, and the junction it is on | `AddTraxGraphQL()` throws: `AddTraxGraphQL() requires AddTrax() to be called first` |
| `.AddMediator(typeof(Program).Assembly)` | Registers every train in the project under its interface | The trains do not exist; `Schedule<IHelloWorldTrain>` refuses to start: `No train implements IServiceTrain<HelloWorldInput, TOut>` |
| `.AddScheduler(...)` with `Schedule<IHelloWorldTrain>("hello-world", ..., Every.Seconds(20))` | Stores the manifest at startup and runs it every 20 seconds | `UseTraxDashboard()` refuses to start: `UseTraxDashboard() requires the Trax Scheduler` |
| `AddDbContextFactory<AppDbContext>(...)` | Your own EF Core context, on an in-memory database | The `EnsureCreated()` bootstrap throws: `No service for type 'IDbContextFactory<AppDbContext>' has been registered` |
| `AddTraxGraphQL(graphql => graphql.AddDbContext<AppDbContext>())` | The schema at `/trax/graphql`: trains under `discover` and `dispatch`, `Note` under `discover { app }` | `UseTraxGraphQL()` throws: `UseTraxGraphQL() requires AddTraxGraphQL() to be called first.` |
| `AddHealthChecks().AddTraxHealthCheck()` | `/trax/health` | `MapHealthChecks` throws, naming `AddHealthChecks` |
| `AddTraxDashboard(d => d.AllowAnonymousDashboard())`, Development only | The dashboard at `/trax`, open to anyone who can reach the port | Without the posture, `UseTraxDashboard()` throws naming `RequirePolicy`, `RequireRoles` and `AllowAnonymousDashboard`; without `AddTraxDashboard` at all, it throws `UseTraxDashboard() requires AddTraxDashboard() to be called first.` |

`AllowAnonymousDashboard()` sits inside the `IsDevelopment()` check, and so do `AddTraxDashboard`
and `UseTraxDashboard`: the dashboard is not served anywhere else until you choose who may use it
(see [Before deploying](#before-deploying)).

The two trains both carry `[TraxAuthorize(Roles = "User")]`:

- **LookupTrain**: `[TraxQuery]`, a query that runs the train and returns its output: `query { discover { lookup(input: { id: "42" }) { id name createdAt } } }`
- **HelloWorldTrain**: `[TraxMutation(GraphQLOperation.Run)]`, a mutation that runs the train and answers when it finishes: `mutation { dispatch { helloWorld(input: { name: "Trax" }) { externalId metadataId } } }`

`HelloWorldTrain` exposes `Run` only on purpose. `[TraxMutation]` with no operation also adds a
`mode: QUEUE` argument, which writes the run to the scheduler's work queue. Only a database-backed
scheduler reads that queue: on the in-memory provider nothing would ever run the entry, so a
`mode: QUEUE` request is refused with `TRAX_QUEUE_UNAVAILABLE` and the host logs a warning at
startup. Switch to Postgres (below) before enabling `Queue`.

The junctions derive `EffectJunction<TIn, TOut>`, because `AddJunctionProgress()` only sees
junctions of that type. See [Junction Progress](/docs/effect/effect-providers/junction-progress).

**Packages:** `Trax.Effect`, `Trax.Effect.Data.InMemory`, `Trax.Effect.Provider.Parameter`,
`Trax.Effect.JunctionProvider.Progress`, `Trax.Mediator`, `Trax.Scheduler`, `Trax.Dashboard`,
`Trax.Api`, `Trax.Api.Auth.ApiKey`, `Trax.Api.GraphQL`.

### trax-api

The `Auth/`, `Data/`, `Trains/` and `tests/` of `trax-hub`, in a host with no scheduler and no
dashboard. `Program.cs` registers the demo key, `AddTrax` with
`UseInMemory().SaveTrainParameters()` and `AddMediator`, the application context, GraphQL and the
health check. The tests have no dashboard checks.

The API runs every train itself, when it is called. Running trains on a timer, or queueing them
for another process, needs a scheduler with the same trains registered and a database both
processes share; `trax-hub` is the one-process version.

**Packages:** `Trax.Effect`, `Trax.Effect.Data.InMemory`, `Trax.Effect.Provider.Parameter`,
`Trax.Mediator`, `Trax.Api`, `Trax.Api.Auth.ApiKey`, `Trax.Api.GraphQL`.

### trax-scheduler

```
MyCompany.Scheduler/
├── MyCompany.Scheduler.csproj
├── Directory.Packages.props
├── Program.cs
├── README.md
├── .gitignore
├── appsettings.json
├── Properties/
│   └── launchSettings.json
├── Trains/
│   └── HelloWorld/
│       ├── IHelloWorldTrain.cs
│       ├── HelloWorldTrain.cs
│       ├── HelloWorldInput.cs
│       └── Junctions/
│           └── LogGreetingJunction.cs
└── tests/
    └── MyCompany.Scheduler.Tests/
        ├── MyCompany.Scheduler.Tests.csproj
        └── IntegrationTests/
            ├── HelloWorldTrainTests.cs
            └── HostTests.cs
```

`Program.cs` registers `AddTrax` with `UseInMemory().SaveTrainParameters().AddJunctionProgress()`,
`AddMediator`, `AddScheduler` with the `hello-world` manifest every 20 seconds, and the dashboard
with `AllowAnonymousDashboard()` in Development only. It has no GraphQL and no authentication.

**Packages:** `Trax.Effect`, `Trax.Effect.Data.InMemory`, `Trax.Effect.Provider.Parameter`,
`Trax.Effect.JunctionProvider.Progress`, `Trax.Mediator`, `Trax.Scheduler`, `Trax.Dashboard`.

## Running

```bash
cd MyCompany.Hub && dotnet run
```

`dotnet run` starts in Development, from the `http` profile in `Properties/launchSettings.json`,
which is also the only place the URL is set:

| Template | Open |
|----------|------|
| `trax-hub` | `http://localhost:5400/trax/graphql` for the GraphQL IDE, `http://localhost:5400/trax` for the dashboard, `/trax/health` |
| `trax-api` | `http://localhost:5402/trax/graphql` for the GraphQL IDE, `/trax/health` |
| `trax-scheduler` | `http://localhost:5401/trax` for the dashboard |

Send `X-Api-Key: demo-key-do-not-use-in-production` with every GraphQL operation:

```bash
curl -X POST http://localhost:5400/trax/graphql \
  -H 'Content-Type: application/json' \
  -H 'X-Api-Key: demo-key-do-not-use-in-production' \
  -d '{"query":"mutation { dispatch { helloWorld(input: { name: \"Trax\" }) { externalId metadataId } } }"}'
```

Without the key, or with a wrong one, the response is HTTP 200 with an error whose
`extensions.code` is `TRAX_AUTHORIZATION`.

The HelloWorld train runs every 20 seconds in `trax-scheduler` and `trax-hub`; the dashboard shows
each run. The in-memory provider keeps its data inside the process, so it is lost on restart and
two processes never see each other's.

Outside `dotnet run` (a published build, a container) the URL comes from `ASPNETCORE_URLS` or
`ASPNETCORE_HTTP_PORTS`, and with neither set Kestrel listens on `http://localhost:5000`.

## Testing

```bash
dotnet test tests/MyCompany.Hub.Tests
```

`dotnet test` at the project root finds only the application project, so name the test project.
Each test project references the application and uses NUnit with
`Microsoft.AspNetCore.Mvc.Testing`:

| Test | What it shows |
|---|---|
| `UnitTests/FetchDataJunctionTests` | A junction constructed with its dependencies and called directly: no container, no database |
| `IntegrationTests/HelloWorldTrainTests` | A train run through `ITrainBus` on a container with `UseInMemory()` and `AddMediator`, then its `Metadata` read back from `IDataContext` |
| `IntegrationTests/HostTests` | `WebApplicationFactory<Program>` starting the whole host in Development and in Production, calling GraphQL with and without the demo key, and checking the dashboard is served only in Development |

`HostTests` is the test that fails when `Program.cs` no longer starts. The application's top-level
`Program` class is public in .NET 10, so `WebApplicationFactory<Program>` needs nothing added. See
[Testing](/docs/cross-cutting/testing) for more patterns.

## Adding Your Own Trains

Put each train in its own folder under `Trains/`, with its interface, input, output and a
`Junctions/` folder, as `HelloWorld/` is laid out. `AddMediator(typeof(Program).Assembly)` finds
it; nothing else is registered.

### Query train (read-only, runs on the API)

```csharp
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace MyCompany.Hub.Trains.GetCustomer;

public interface IGetCustomerTrain : IServiceTrain<GetCustomerInput, CustomerOutput>;

[TraxAuthorize(Roles = "User")]
[TraxQuery(Description = "Fetches a customer by ID")]
public class GetCustomerTrain
    : ServiceTrain<GetCustomerInput, CustomerOutput>, IGetCustomerTrain
{
    protected override Task<Either<Exception, CustomerOutput>> Junctions() =>
        Chain<FetchCustomerJunction>().Resolve();
}
```

The field is `getCustomer` under `query { discover { ... } }`: the interface name without the `I`
and the `Train`, in camelCase. The host refuses to start if an exposed train has neither `[TraxAuthorize]` nor
`[TraxAllowAnonymous]`, and if a junction needs an input that nothing before it produces.

### Scheduled train (runs on the scheduler)

```csharp
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace MyCompany.Scheduler.Trains.SyncCustomers;

public record SyncCustomersInput : IManifestProperties
{
    public string Region { get; init; } = "us-east";
}

public interface ISyncCustomersTrain : IServiceTrain<SyncCustomersInput, Unit>;

public class SyncCustomersTrain : ServiceTrain<SyncCustomersInput, Unit>, ISyncCustomersTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<FetchCustomersJunction>()
            .Chain<UpsertCustomersJunction>()
            .Resolve();
}
```

A scheduled train's input implements `IManifestProperties`, so the scheduler can store it. Add the
schedule after the `hello-world` one in `Program.cs`:

```csharp
scheduler
    .Schedule<ISyncCustomersTrain>(
        "sync-customers",
        new SyncCustomersInput { Region = "us-east" },
        Every.Hours(1)
    );
```

The first argument is the manifest's id: `Schedule` updates the manifest with that id, so renaming
it creates a new one. See [Scheduling](/docs/scheduler) for cron schedules, dependent trains, bulk
scheduling and dead letters.

## Switching to Postgres

```bash
docker run -d --name trax-db -p 127.0.0.1:5432:5432 \
  -e POSTGRES_USER=trax -e POSTGRES_PASSWORD=trax123 -e POSTGRES_DB=trax postgres:17
dotnet add package Trax.Effect.Data.Postgres --version <the Trax.Effect version>
```

Put the connection string in `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "TraxDatabase": "Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123"
  }
}
```

In `Program.cs`, add `using Trax.Effect.Data.Postgres.Extensions;` and replace `UseInMemory()`:

```csharp
effects.UsePostgres(
    builder.Configuration.GetConnectionString("TraxDatabase")
        ?? throw new InvalidOperationException("Set ConnectionStrings:TraxDatabase.")
)
```

Trax creates and migrates its `trax` schema at startup. The scheduler then queues work in the
database and runs it on local workers, so `[TraxMutation]` with `mode: QUEUE` runs, and several
processes on one database share the work. The application context (`AppDbContext`) is separate:
switch its `UseInMemoryDatabase("app")` to `UseNpgsql(...)` (package
`Npgsql.EntityFrameworkCore.PostgreSQL`) when you want it in Postgres too, under the schema in
`Data/AppSchema.cs`. See [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect).

## Before deploying

The templates start in Development: `dotnet run` picks up `ASPNETCORE_ENVIRONMENT=Development`
from `Properties/launchSettings.json`. Two things exist only there.

| What | Where | Outside Development |
|------|-------|---------------------|
| The Trax Dashboard at `/trax` | `trax-scheduler`, `trax-hub` | Not mapped; `/trax` is a 404. |
| The demo API key (`X-Api-Key: demo-key-do-not-use-in-production`) | `trax-api`, `trax-hub` | Not registered. Every template train and the `Note` query model carry `[TraxAuthorize]`, so every operation is refused. |

The dashboard can queue, run and cancel trains and change scheduler settings, and
`AllowAnonymousDashboard()` puts no authorization in front of it. To serve it anywhere else,
register an authentication scheme, choose who may use it with `RequirePolicy("<policy>")` or
`RequireRoles("<role>")` in place of `AllowAnonymousDashboard()` (see
[UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard)), and remove the
`IsDevelopment()` checks around `AddTraxDashboard` and `UseTraxDashboard`. A policy or roles with
no authentication scheme to challenge with refuses to start, naming `AddAuthentication`, because
every request the dashboard refused would otherwise be a 500 rather than a 401 or 403.

Replace the demo key with real credentials, such as `AddHashed` keys loaded from a secret store
or [`AddTraxJwtAuth`](/docs/sdk-reference/api-auth/add-trax-jwt-auth), before removing the check
around `AddTraxApiKeyAuth`. Moving the demo key out of the check does not work: a host refuses to
start outside Development with a key containing `do-not-use-in-production` registered. Keep
`[TraxAuthorize]` on the trains you add; mark one `[TraxAllowAnonymous]` only when anyone who can
reach the host may call it. See [API Security](/docs/api-security).

A published build started with `dotnet MyCompany.Hub.dll`, or in a container, runs in Production
unless the environment variable says otherwise, so it gets neither.

## Uninstalling

```bash
dotnet new uninstall Trax.Samples.Templates
```

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect) | [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard) | [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth)
