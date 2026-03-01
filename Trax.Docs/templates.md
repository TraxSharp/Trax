---
layout: default
title: Project Template
nav_order: 12
---

# Project Template

Trax.Core ships a `dotnet new` template that scaffolds a working server with Hangfire scheduling, PostgreSQL persistence, the Trax.Core Dashboard, and a starter workflow—ready to run out of the box.

## Installation

Install from NuGet:

```bash
dotnet new install Trax.Samples.Templates
```

Or install from a local clone of the repository:

```bash
dotnet new install ./templates/content/Trax.Samples.Server/
```

## Creating a Project

```bash
dotnet new trax-server --name MyCompany.OrderService
```

This creates a `MyCompany.OrderService/` directory with all namespaces, filenames, and the csproj set to `MyCompany.OrderService`.

### Custom Connection String

By default the template uses a local development connection string. Override it at creation time:

```bash
dotnet new trax-server --name MyCompany.OrderService \
    --ConnectionString "Host=db.example.com;Port=5432;Database=orders;Username=app;Password=secret"
```

## What You Get

```
MyCompany.OrderService/
├── MyCompany.OrderService.csproj
├── Program.cs
├── appsettings.json
├── Properties/
│   └── launchSettings.json
└── Workflows/
    └── HelloWorld/
        ├── HelloWorldInput.cs
        ├── HelloWorldWorkflow.cs
        ├── IHelloWorldWorkflow.cs
        └── Steps/
            └── LogGreetingStep.cs
```

### Program.cs

A minimal `WebApplication` configured with:

- **Trax.Core Effects** — workflow bus, PostgreSQL persistence, JSON and parameter providers
- **Scheduler** — Hangfire backend with a HelloWorld job running every 20 seconds
- **Dashboard** — Trax.Core Dashboard at `/` and Hangfire Dashboard at `/hangfire`
- **Metadata Cleanup** — automatic cleanup of old execution records

### HelloWorld Workflow

A single workflow with one step that logs a greeting. Use it as a reference for building your own workflows, then delete it when you're ready.

```csharp
public class HelloWorldWorkflow : ServiceTrain<HelloWorldInput, Unit>, IHelloWorldWorkflow
{
    protected override async Task<Either<Exception, Unit>> RunInternal(HelloWorldInput input) =>
        Activate(input).Chain<LogGreetingStep>().Resolve();
}
```

*API Reference: [Activate]({{ site.baseurl }}{% link api-reference/workflow-methods/activate.md %}), [Chain]({{ site.baseurl }}{% link api-reference/workflow-methods/chain.md %}), [Resolve]({{ site.baseurl }}{% link api-reference/workflow-methods/resolve.md %})*

### Package References

The generated csproj references all Trax.Core packages at `5.*`, so you'll automatically pick up patch and minor updates:

```xml
<PackageReference Include="Trax.Effect" Version="5.*" />
<PackageReference Include="Trax.Effect.Data.Postgres" Version="5.*" />
<PackageReference Include="Trax.Mediator" Version="5.*" />
<PackageReference Include="Trax.Scheduler" Version="5.*" />
<PackageReference Include="Trax.Scheduler.Hangfire" Version="5.*" />
<PackageReference Include="Trax.Effect.Provider.Json" Version="5.*" />
<PackageReference Include="Trax.Effect.Provider.Parameter" Version="5.*" />
<PackageReference Include="Trax.Dashboard" Version="5.*" />
```

## Running

1. Start PostgreSQL (the connection string in `appsettings.json` points to `localhost:5432` by default)
2. Run the project:

```bash
dotnet run
```

3. Open `http://localhost:5000` for the Trax.Core Dashboard
4. Open `http://localhost:5000/hangfire` for the Hangfire Dashboard

The HelloWorld job will start running every 20 seconds. Check the dashboards to see execution records.

## Adding Your Own Workflows

1. Create an input record implementing `IManifestProperties`:

```csharp
public record SyncCustomersInput : IManifestProperties
{
    public string Region { get; init; } = "us-east";
}
```

2. Define a workflow interface and implementation:

```csharp
public interface ISyncCustomersWorkflow : IServiceTrain<SyncCustomersInput, Unit>;

public class SyncCustomersWorkflow : ServiceTrain<SyncCustomersInput, Unit>, ISyncCustomersWorkflow
{
    protected override async Task<Either<Exception, Unit>> RunInternal(SyncCustomersInput input) =>
        Activate(input)
            .Chain<FetchCustomersStep>()
            .Chain<UpsertCustomersStep>()
            .Resolve();
}
```

3. Register the cleanup and schedule in `Program.cs`:

```csharp
scheduler
    .AddMetadataCleanup(cleanup =>
    {
        cleanup.AddWorkflowType<IHelloWorldWorkflow>();
        cleanup.AddWorkflowType<ISyncCustomersWorkflow>();
    })
    .UseHangfire(connectionString)
    .Schedule<ISyncCustomersWorkflow, SyncCustomersInput>(
        "sync-customers",
        new SyncCustomersInput { Region = "us-east" },
        Every.Hours(1)
    );
```

*API Reference: [AddMetadataCleanup]({{ site.baseurl }}{% link api-reference/scheduler-api/add-metadata-cleanup.md %}), [UseHangfire]({{ site.baseurl }}{% link api-reference/scheduler-api/use-hangfire.md %}), [Schedule]({{ site.baseurl }}{% link api-reference/scheduler-api/schedule.md %})*

See [Scheduling](scheduler.md) for dependent workflows, bulk scheduling, and dead letter handling.

## Uninstalling

```bash
dotnet new uninstall Trax.Samples.Templates
```
