---
layout: default
title: Project Templates
parent: Reference
nav_order: 4
---

# Project Templates

Trax ships three `dotnet new` templates in the `Trax.Samples.Templates` package. Each one runs
with `dotnet run` and nothing else: they use the in-memory data provider, so no database is needed
until you choose one.

| Template | Short name | What it is | Port |
|----------|------------|------------|------|
| Trax GraphQL API | `trax-api` | A GraphQL API with a typed query train and a mutation train | 5002 |
| Trax Scheduler with Dashboard | `trax-scheduler` | A scheduler running one train every 20 seconds, with the Trax Dashboard | 5001 |
| Trax Hub | `trax-hub` | The API, the scheduler and the dashboard in one process | 5000 |

`trax-hub` is the one [`trax generate`](/docs/reference/cli) scaffolds.

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

## What You Get

### trax-api

```
MyCompany.Api/
├── MyCompany.Api.csproj
├── Program.cs
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
└── Trains/
    ├── Lookup/
    │   ├── ILookupTrain.cs
    │   ├── LookupTrain.cs
    │   ├── LookupInput.cs
    │   ├── LookupOutput.cs
    │   └── Junctions/
    │       └── FetchDataJunction.cs
    └── HelloWorld/
        ├── IHelloWorldTrain.cs
        ├── HelloWorldTrain.cs
        ├── HelloWorldInput.cs
        └── Junctions/
            └── LogGreetingJunction.cs
```

**Program.cs** configures:

- **Trax Effects**: the in-memory data provider and the mediator
- **Authentication**: API-key auth with a demo key, in Development only (see [Before deploying](#before-deploying))
- **Application data**: an `AppDbContext` on an in-memory EF Core database, exposed to GraphQL
- **GraphQL API**: HotChocolate schema at `/trax/graphql` with the Banana Cake Pop IDE
- **Health check**: `/trax/health`

**Sample trains:** both carry `[TraxAuthorize(Roles = "User")]`, the role the demo key holds,
so every request needs `X-Api-Key: demo-key-do-not-use-in-production` (see
[Before deploying](#before-deploying)).

- **LookupTrain**: a `[TraxQuery]` train that returns typed output. Generates a query field: `query { discover { lookup(input: { id: "42" }) { id name createdAt } } }`
- **HelloWorldTrain**: a `[TraxMutation]` train that logs a greeting. Generates a mutation field: `mutation { dispatch { helloWorld(input: { name: "Trax" }) { externalId metadataId } } }`

**Packages:** `Trax.Effect`, `Trax.Effect.Data.InMemory`, `Trax.Effect.Provider.Json`,
`Trax.Effect.Provider.Parameter`, `Trax.Mediator`, `Trax.Api`, `Trax.Api.Auth.ApiKey`,
`Trax.Api.GraphQL`.

### trax-scheduler

```
MyCompany.Scheduler/
├── MyCompany.Scheduler.csproj
├── Program.cs
├── appsettings.json
├── Properties/
│   └── launchSettings.json
└── Trains/
    └── HelloWorld/
        ├── IHelloWorldTrain.cs
        ├── HelloWorldTrain.cs
        ├── HelloWorldInput.cs
        └── Junctions/
            └── LogGreetingJunction.cs
```

**Program.cs** configures:

- **Trax Effects**: the in-memory data provider and the mediator
- **Scheduler**: a HelloWorld job running every 20 seconds
- **Dashboard**: the Trax Dashboard at `/trax`, in Development only (see [Before deploying](#before-deploying))

**Packages:** `Trax.Effect`, `Trax.Effect.Data.InMemory`, `Trax.Effect.Provider.Json`,
`Trax.Effect.Provider.Parameter`, `Trax.Effect.JunctionProvider.Progress`, `Trax.Mediator`,
`Trax.Scheduler`, `Trax.Dashboard`.

### trax-hub

The files of `trax-api` (the same `Auth/`, `Data/` and `Trains/` folders) in one project that
also runs the scheduler and the dashboard.

**Program.cs** configures everything the other two do, in one process: the GraphQL API at
`/trax/graphql`, the demo key and the dashboard at `/trax` in Development only, the HelloWorld
job every 20 seconds, and the health check at `/trax/health`.

**Packages:** the union of the two above.

## Before deploying

The templates start in Development: `dotnet run` picks up `ASPNETCORE_ENVIRONMENT=Development`
from `Properties/launchSettings.json`. Two things exist only there.

| What | Where | Outside Development |
|------|-------|---------------------|
| The Trax Dashboard at `/trax` | `trax-scheduler`, `trax-hub` | Not mapped; `/trax` is a 404. |
| The demo API key (`X-Api-Key: demo-key-do-not-use-in-production`) | `trax-api`, `trax-hub` | Not registered. Every template train and the `Note` query model carry `[TraxAuthorize]`, so every operation is refused. |

The dashboard can queue, run and cancel trains and change scheduler settings, and the
templates put no authorization in front of it. To serve it anywhere else, choose who may use
it (see [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard)) and remove
the `IsDevelopment()` check around `AddTraxDashboard` and `UseTraxDashboard` in `Program.cs`.
Replace the demo key with real credentials, such as `AddHashed` keys loaded from a secret
store, before removing the check around `AddTraxApiKeyAuth`. Keep `[TraxAuthorize]` on the
trains you add; mark one `[TraxAllowAnonymous]` only when anyone who can reach the host may
call it.

A published build started with `dotnet MyCompany.Scheduler.dll`, or in a container, runs in
Production unless the environment variable says otherwise, so it gets neither.

## Running

```bash
cd MyCompany.Hub && dotnet run
```

| Template | Open |
|----------|------|
| `trax-api` | `http://localhost:5002/trax/graphql` for the GraphQL IDE |
| `trax-scheduler` | `http://localhost:5001/trax` for the dashboard |
| `trax-hub` | `http://localhost:5000/trax/graphql` for the GraphQL IDE, `http://localhost:5000/trax` for the dashboard |

The HelloWorld train starts running every 20 seconds in `trax-scheduler` and `trax-hub`; the
dashboard shows each run.

The in-memory provider keeps its data inside the process, so it is lost on restart and two
processes never see each other's. A `{trainName}(mode: QUEUE)` mutation on `trax-api` alone
queues work that no scheduler reads. To run the API and the scheduler as separate processes,
point both at one database: replace `UseInMemory()` with
[`UsePostgres(connectionString)`](/docs/sdk-reference/configuration/add-postgres-effect) or
[`UseSqlite(connectionString)`](/docs/sdk-reference/configuration/use-sqlite) and add the matching
`Trax.Effect.Data.*` package. Until then, `trax-hub` is the template where queueing works.

## Adding Your Own Trains

### Query train (read-only, runs on the API)

```csharp
[TraxAuthorize(Roles = "User")]
[TraxQuery(Description = "Fetches a customer by ID")]
public class GetCustomerTrain
    : ServiceTrain<GetCustomerInput, CustomerOutput>, IGetCustomerTrain
{
    protected override Task<Either<Exception, CustomerOutput>> Junctions() =>
        Chain<FetchCustomerJunction>().Resolve();
}
```

### Scheduled train (runs on the scheduler)

```csharp
public record SyncCustomersInput : IManifestProperties
{
    public string Region { get; init; } = "us-east";
}

public class SyncCustomersTrain : ServiceTrain<SyncCustomersInput, Unit>, ISyncCustomersTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<FetchCustomersJunction>()
            .Chain<UpsertCustomersJunction>()
            .Resolve();
}
```

Register the schedule in `Program.cs`:

```csharp
scheduler
    .Schedule<ISyncCustomersTrain>(
        "sync-customers",
        new SyncCustomersInput { Region = "us-east" },
        Every.Hours(1)
    );
```

See [Scheduling](/docs/scheduler) for dependent trains, bulk scheduling, and dead letter handling.

## Uninstalling

```bash
dotnet new uninstall Trax.Samples.Templates
```

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect) | [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) | [AddMediator](/docs/sdk-reference/mediator-api/add-service-train-bus) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)
