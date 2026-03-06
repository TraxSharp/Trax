---
layout: default
title: Samples & Deployment
nav_order: 10
---

# Samples & Deployment Patterns

The Trax samples demonstrate a consistent architectural pattern: **put your trains in a library, then wrap them with thin executables**. Each executable is just a `Program.cs` that picks which Trax capabilities to wire up. The trains themselves stay deployment-agnostic — the same library powers a standalone scheduler, a GraphQL API, a distributed worker fleet, or all three at once.

This mirrors the ladder philosophy. You only add the packages you need, and you only build the executables you need. The trains don't change.

## The Trains Library Pattern

Every sample follows the same two-layer split:

```
MyApp/                          ← library (class library, not executable)
  Trains/
    Feature1/
      IFeature1Train.cs
      Feature1Train.cs
      Feature1Input.cs
      Steps/
    Feature2/
      ...
  ManifestNames.cs

MyApp.Scheduler/                ← executable (thin wrapper)
  Program.cs
  appsettings.json

MyApp.Api/                      ← executable (thin wrapper)
  Program.cs
  appsettings.json
```

### The Library

The library project contains everything that defines *what your application does*:

- **Trains** — `ServiceTrain<TIn, TOut>` implementations with their steps
- **Interfaces** — `IServiceTrain<TIn, TOut>` contracts for each train
- **Inputs and outputs** — POCOs that define each train's data contract
- **ManifestNames** — string constants for scheduler manifest IDs
- **Domain types** — any shared models, enums, or utilities

The library references `Trax.Effect`, `Trax.Mediator`, and `Trax.Scheduler` (or whatever layers your trains need), but it does **not** reference infrastructure packages like `Trax.Dashboard`, `Trax.Api.GraphQL`, or the effect providers. It has no `Program.cs` and no `appsettings.json`.

```xml
<!-- Library .csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Trax.Effect" Version="1.*" />
    <PackageReference Include="Trax.Effect.Data.Postgres" Version="1.*" />
    <PackageReference Include="Trax.Mediator" Version="1.*" />
    <PackageReference Include="Trax.Scheduler" Version="1.*" />
  </ItemGroup>
</Project>
```

### The Executables

Each executable is a `Microsoft.NET.Sdk.Web` project with a `ProjectReference` to the library. Its `Program.cs` calls `AddTrax()` and configures whichever capabilities this process needs — scheduling, dashboard, GraphQL, worker polling, or any combination.

```xml
<!-- Executable .csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyApp\MyApp.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Trax.Effect.Provider.Json" Version="1.*" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" Version="1.*" />
    <PackageReference Include="Trax.Effect.StepProvider.Progress" Version="1.*" />
    <PackageReference Include="Trax.Dashboard" Version="1.*" />
  </ItemGroup>
</Project>
```

The key line in `Program.cs` is the assembly scan — it points at the library so the train bus discovers all your trains:

```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        // ... add whatever this executable needs
    )
    .AddMediator(typeof(ManifestNames).Assembly, ...)
);
```

Different executables add different capabilities on top of the same trains. That's the entire pattern.

## Deployment Models

The three sample directories show three deployment topologies, each built on the same pattern.

### Model 1: Standalone Scheduler

**Sample:** `DataPipeline/`

```
Trax.Samples.Flowthru.Spaceflights/           ← library (trains)
Trax.Samples.Flowthru.Spaceflights.Scheduler/ ← executable (scheduler + dashboard)
```

The simplest deployment — one process that schedules and executes everything. The executable adds `AddScheduler()` with `UseLocalWorkers()` and `AddTraxDashboard()`.

Good for: data pipelines, ETL jobs, background processing where a single server handles the load.

### Model 2: Separate API + Scheduler

**Sample:** `LocalWorkers/`

```
Trax.Samples.GameServer/            ← library (trains)
Trax.Samples.GameServer.Scheduler/  ← executable (scheduler + dashboard)
Trax.Samples.GameServer.Api/        ← executable (API only)
```

Two processes share the same trains library. The scheduler process handles background execution and hosts the dashboard. The GraphQL process serves the API — it can run lightweight trains synchronously and queue heavy work for the scheduler.

The split is simple:
- **Scheduler:** `AddScheduler()` + `UseLocalWorkers()` + `AddTraxDashboard()`
- **API:** `AddTraxGraphQL()` — no scheduler, no executor

Good for: web applications with both an API and background jobs, where the API needs to stay responsive and offload heavy work.

### Model 3: Hub + Distributed Workers

**Sample:** `DistributedWorkers/`

```
Trax.Samples.EnergyHub/         ← library (trains)
Trax.Samples.EnergyHub.Hub/     ← executable (API + scheduler + dashboard, no execution)
Trax.Samples.EnergyHub.Worker/  ← executable (worker only, no API)
```

The hub process manages scheduling and serves the API, but does **not** execute trains. Instead, it writes jobs to the `background_job` table via PostgresJobSubmitter (the default when `UseLocalWorkers()` is omitted). Separate worker processes poll that table and execute trains.

The split:
- **Hub:** `AddScheduler()` (without `UseLocalWorkers()`) + `AddTraxGraphQL()` + `AddTraxDashboard()`
- **Worker:** `AddTraxWorker()` — polls `background_job` with `FOR UPDATE SKIP LOCKED`

Workers scale horizontally — run as many as you need. The hub stays lightweight.

Good for: high-throughput systems, microservices, environments where you need to scale execution independently from scheduling.

## Comparing the Models

| Capability | Standalone | Separate API | Distributed |
|-----------|-----------|-------------|------------|
| Processes | 1 | 2 | 2+ |
| Scheduler | In-process | In-process (scheduler) | Hub (scheduling only) |
| Execution | In-process | In-process (scheduler) | Workers (polling) |
| API | None | Separate process | Hub |
| Dashboard | In-process | In scheduler | In hub |
| Horizontal scaling | No | No | Workers scale independently |

In all three models, the trains library is identical. Only the `Program.cs` files differ.

## Running the Samples

All samples require PostgreSQL. From the `Trax.Samples/` directory:

```bash
docker compose up -d
```

### DataPipeline (Standalone)

```bash
dotnet run --project samples/DataPipeline/Trax.Samples.Flowthru.Spaceflights.Scheduler
```

Dashboard at `http://localhost:5000/trax`.

### LocalWorkers (Separate API + Scheduler)

```bash
# Terminal 1 — scheduler
dotnet run --project samples/LocalWorkers/Trax.Samples.GameServer.Scheduler

# Terminal 2 — API
dotnet run --project samples/LocalWorkers/Trax.Samples.GameServer.Api
```

Dashboard at `http://localhost:5201/trax`. GraphQL IDE at `http://localhost:5200/trax/graphql`.

### DistributedWorkers (Hub + Workers)

```bash
# Terminal 1 — hub
dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Hub

# Terminal 2 — worker
dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Worker
```

Dashboard at `http://localhost:5202/trax`. GraphQL IDE at `http://localhost:5202/trax/graphql`.
