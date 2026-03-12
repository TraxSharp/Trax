---
layout: default
title: Project Templates
parent: Reference
nav_order: 4
---

# Project Templates

Trax ships two `dotnet new` templates that scaffold working projects with PostgreSQL persistence, ready to run out of the box:

- **`trax-api`** — a GraphQL API with typed query and mutation trains
- **`trax-scheduler`** — a scheduler with the Trax Dashboard

They're designed to work together against a shared PostgreSQL database — the API handles lightweight operations directly and queues heavy work for the scheduler.

## Installation

Install from NuGet:

```bash
dotnet new install Trax.Samples.Templates
```

## Creating Projects

### GraphQL API

```bash
dotnet new trax-api --name MyCompany.Api
```

### Scheduler with Dashboard

```bash
dotnet new trax-scheduler --name MyCompany.Scheduler
```

Both commands create a directory with all namespaces, filenames, and the csproj set to your project name.

### Custom Connection String

By default the templates use a local development connection string. Override it at creation time:

```bash
dotnet new trax-api --name MyCompany.Api \
    --ConnectionString "Host=db.example.com;Port=5432;Database=orders;Username=app;Password=secret"
```

## What You Get

### trax-api

```
MyCompany.Api/
├── MyCompany.Api.csproj
├── Program.cs
├── appsettings.json
├── Properties/
│   └── launchSettings.json
└── Trains/
    ├── Lookup/
    │   ├── ILookupTrain.cs
    │   ├── LookupTrain.cs
    │   ├── LookupInput.cs
    │   ├── LookupOutput.cs
    │   └── Steps/
    │       └── FetchDataStep.cs
    └── HelloWorld/
        ├── IHelloWorldTrain.cs
        ├── HelloWorldTrain.cs
        ├── HelloWorldInput.cs
        └── Steps/
            └── LogGreetingStep.cs
```

**Program.cs** configures:

- **Trax Effects** — train bus, PostgreSQL persistence, JSON and parameter providers
- **GraphQL API** — HotChocolate schema at `/trax/graphql` with Banana Cake Pop IDE
- **Health check** — ASP.NET Core health endpoint at `/trax/health`

**Sample trains:**

- **LookupTrain** — a `[TraxQuery]` train that returns typed output. Generates a query field: `query { discover { lookup(input: { id: "42" }) { id name createdAt } } }`
- **HelloWorldTrain** — a `[TraxMutation]` train that logs a greeting. Generates a mutation field: `mutation { dispatch { helloWorld(input: { name: "Trax" }) { externalId metadataId } } }`

**Packages:**

```xml
<PackageReference Include="Trax.Effect" Version="1.*" />
<PackageReference Include="Trax.Effect.Data.Postgres" Version="1.*" />
<PackageReference Include="Trax.Effect.Provider.Json" Version="1.*" />
<PackageReference Include="Trax.Effect.Provider.Parameter" Version="1.*" />
<PackageReference Include="Trax.Mediator" Version="1.*" />
<PackageReference Include="Trax.Api" Version="1.*" />
<PackageReference Include="Trax.Api.GraphQL" Version="1.*" />
```

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
        └── Steps/
            └── LogGreetingStep.cs
```

**Program.cs** configures:

- **Trax Effects** — train bus, PostgreSQL persistence, JSON and parameter providers, step progress tracking
- **Scheduler** — PostgreSQL local workers with a HelloWorld job running every 20 seconds
- **Dashboard** — Trax Blazor Dashboard at `/trax`

**Packages:**

```xml
<PackageReference Include="Trax.Effect" Version="1.*" />
<PackageReference Include="Trax.Effect.Data.Postgres" Version="1.*" />
<PackageReference Include="Trax.Effect.Provider.Json" Version="1.*" />
<PackageReference Include="Trax.Effect.Provider.Parameter" Version="1.*" />
<PackageReference Include="Trax.Effect.StepProvider.Progress" Version="1.*" />
<PackageReference Include="Trax.Mediator" Version="1.*" />
<PackageReference Include="Trax.Scheduler" Version="1.*" />
<PackageReference Include="Trax.Dashboard" Version="1.*" />
```

## Running

### API only

1. Start PostgreSQL (the connection string in `appsettings.json` points to `localhost:5432` by default)
2. Run the project: `dotnet run`
3. Open `http://localhost:5200/trax/graphql` for the GraphQL IDE

### Scheduler only

1. Start PostgreSQL
2. Run the project: `dotnet run`
3. Open `http://localhost:5201/trax` for the Dashboard

The HelloWorld train will start running every 20 seconds. Check the dashboard to see execution records.

### Both together

The two templates are designed to run side-by-side against the same database:

```bash
# Terminal 1 — Scheduler
cd MyCompany.Scheduler && dotnet run

# Terminal 2 — API
cd MyCompany.Api && dotnet run
```

The API can queue trains for the scheduler via `{trainName}(mode: QUEUE)` mutations and run lightweight trains directly via `{trainName}` mutations (default mode is `RUN`).

## Adding Your Own Trains

### Query train (read-only, runs on the API)

```csharp
[TraxQuery(Description = "Fetches a customer by ID")]
public class GetCustomerTrain
    : ServiceTrain<GetCustomerInput, CustomerOutput>, IGetCustomerTrain
{
    protected override async Task<Either<Exception, CustomerOutput>> RunInternal(
        GetCustomerInput input
    ) => Activate(input).Chain<FetchCustomerStep>().Resolve();
}
```

*SDK Reference: [TraxQuery & TraxMutation Attributes]({{ site.baseurl }}{% link sdk-reference/graphql-api/trax-graphql-attribute.md %})*

### Scheduled train (runs on the scheduler)

```csharp
public record SyncCustomersInput : IManifestProperties
{
    public string Region { get; init; } = "us-east";
}

public class SyncCustomersTrain : ServiceTrain<SyncCustomersInput, Unit>, ISyncCustomersTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(SyncCustomersInput input) =>
        Activate(input)
            .Chain<FetchCustomersStep>()
            .Chain<UpsertCustomersStep>()
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

*SDK Reference: [Schedule]({{ site.baseurl }}{% link sdk-reference/scheduler-api/schedule.md %})*

See [Scheduling]({{ site.baseurl }}{% link scheduler.md %}) for dependent trains, bulk scheduling, and dead letter handling.

## Uninstalling

```bash
dotnet new uninstall Trax.Samples.Templates
```
