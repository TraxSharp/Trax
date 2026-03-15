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

The four sample directories show four deployment topologies, each built on the same pattern.

### Model 1: Standalone Scheduler

**Sample:** `DataPipeline/`

```
Trax.Samples.Flowthru.Spaceflights/           ← library (trains)
Trax.Samples.Flowthru.Spaceflights.Scheduler/ ← executable (scheduler + dashboard)
```

The simplest deployment — one process that schedules and executes everything. The executable adds `AddScheduler()` and `AddTraxDashboard()`. Local workers are the implicit default when `UsePostgres()` is configured.

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
- **Scheduler:** `AddScheduler()` + `AddTraxDashboard()`
- **API:** `AddTraxGraphQL()` — no scheduler, no executor

Good for: web applications with both an API and background jobs, where the API needs to stay responsive and offload heavy work.

### Model 3: Hub + Distributed Workers

**Sample:** `DistributedWorkers/`

```
Trax.Samples.EnergyHub/         ← library (trains)
Trax.Samples.EnergyHub.Hub/     ← executable (API + scheduler + dashboard, no execution)
Trax.Samples.EnergyHub.Worker/  ← executable (worker only, no API)
```

The hub process manages scheduling and serves the API, but does **not** execute trains locally. Instead, it uses `OverrideSubmitter<PostgresJobSubmitter>()` to write jobs to the `background_job` table via PostgresJobSubmitter, bypassing the default local workers. Separate worker processes poll that table and execute trains.

The split:
- **Hub:** `AddScheduler()` + `OverrideSubmitter<PostgresJobSubmitter>()` + `AddTraxGraphQL()` + `AddTraxDashboard()`
- **Worker:** `AddTraxWorker()` — polls `background_job` with `FOR UPDATE SKIP LOCKED`

Workers scale horizontally — run as many as you need. The hub stays lightweight.

Good for: high-throughput systems, microservices, environments where you need to scale execution independently from scheduling.

### Model 4: Ephemeral Workers (Serverless)

**Sample:** `EphemeralWorkers/`

```
Trax.Samples.ContentShield/            ← library (trains)
Trax.Samples.ContentShield.Api/        ← executable (API + dashboard, HTTP dispatch)
Trax.Samples.ContentShield.Runner/     ← executable (ephemeral runner, no scheduler)
```

No scheduled jobs — all work is triggered by GraphQL mutations. The API dispatches queued mutations directly to the Runner via HTTP using `UseRemoteWorkers()`, and also offloads synchronous `run` mutations to the Runner via `UseRemoteRun()`. The Runner simulates a serverless function (AWS Lambda, Cloud Run, Azure Functions) — it receives requests over HTTP, executes the train, and returns.

The split:
- **API:** `AddScheduler()` + `UseRemoteWorkers()` + `UseRemoteRun()` + `AddTraxGraphQL()` + `AddTraxDashboard()`
- **Runner:** `AddTraxJobRunner()` + `UseTraxRunEndpoint()` + `UseBroadcaster()` — no scheduler, no polling, no dashboard

Query trains (e.g. `LookupModerationResult`) run synchronously on the API process. Queued trains (e.g. `ReviewContent`, `SendViolationNotice`) are POSTed to the Runner via `HttpJobSubmitter`. No `background_job` table is involved — jobs go directly over HTTP.

The Runner uses `UseBroadcaster(b => b.UseRabbitMq(...))` to publish lifecycle events back to RabbitMQ, so the API's GraphQL subscriptions are notified when queued trains complete.

Good for: serverless/FaaS deployments, on-demand workloads with zero idle cost, event-driven architectures where all work is API-triggered.

### Model 5: Single-Server with Real-Time Subscriptions

**Sample:** `ChatService/`

```
Trax.Samples.ChatService.Data/     ← data layer (EF Core entities, DbContext, migrations)
Trax.Samples.ChatService/          ← library (trains, lifecycle hook, subscription types)
Trax.Samples.ChatService.Api/      ← executable (single server)
Trax.Samples.ChatService.Client/   ← React + TypeScript frontend (Apollo Client, graphql-ws)
```

A single-server chat application that demonstrates how Trax lifecycle hooks can power domain-specific real-time GraphQL subscriptions. No scheduler or workers — everything runs in one process.

The key innovation is the `ChatLifecycleHook`, a custom `ITrainLifecycleHook` that intercepts completed chat mutation trains. When a `SendMessage` train completes, the hook reads `metadata.Output` (the serialized train output), extracts the `chatRoomId`, and publishes a `ChatSubscriptionEvent` to a room-scoped HotChocolate topic. Clients subscribed to that room receive the event in real time.

This approach works because:
- Chat mutation trains are decorated with `[TraxBroadcast]`, which causes lifecycle hooks to fire
- The hook is registered via `AddLifecycleHook<ChatLifecycleHookFactory>()` on the effect builder
- A custom `ChatSubscriptions` type extends the "trax" GraphQL schema with `onChatEvent(chatRoomId: "...")` alongside the standard Trax lifecycle subscriptions

The sample also includes its own EF Core data layer in a separate project (`ChatService.Data`) with `ChatRoom`, `ChatParticipant`, and `ChatMessage` entities. The `ChatDbContext` uses the `chat` schema to coexist with Trax's `trax` schema in the same database.

A React + TypeScript frontend (`ChatService.Client`) demonstrates the full client/server GraphQL interaction. It uses Apollo Client with a split link — HTTP for queries/mutations and `graphql-ws` for subscriptions — connecting to the HotChocolate endpoint at `localhost:5210/trax/graphql`. The UI lets you switch between users (Alice, Bob, Charlie), create and join rooms, send messages, and see real-time subscription delivery in action.

Good for: real-time applications, chat systems, collaboration tools, notification feeds — anywhere you need domain-specific subscriptions driven by train completion events.

## Comparing the Models

| Capability | Standalone | Separate API | Distributed | Ephemeral | Chat (Real-Time) |
|-----------|-----------|-------------|------------|-----------|-----------------|
| Processes | 1 | 2 | 2+ | 2 | 1 |
| Scheduler | In-process | In-process (scheduler) | Hub (scheduling only) | API (dispatch only) | None |
| Execution | In-process | In-process (scheduler) | Workers (polling) | Runner (HTTP push) | In-process |
| API | None | Separate process | Hub | In-process | In-process |
| Dashboard | In-process | In scheduler | In hub | In API | None |
| Job table | `background_job` | `background_job` | `background_job` | None (direct HTTP) | None |
| Horizontal scaling | No | No | Workers scale independently | Runner auto-scales | No |
| Custom subscriptions | No | No | No | No | Lifecycle hook → topics |

In all models, the trains library is identical. Only the `Program.cs` files differ.

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

### EphemeralWorkers (API + Serverless Runner)

```bash
# Terminal 1 — runner
dotnet run --project samples/EphemeralWorkers/Trax.Samples.ContentShield.Runner

# Terminal 2 — API
dotnet run --project samples/EphemeralWorkers/Trax.Samples.ContentShield.Api
```

Dashboard at `http://localhost:5204/trax`. GraphQL IDE at `http://localhost:5204/trax/graphql`.

### ChatService (Single-Server Real-Time)

```bash
# Terminal 1 — API
dotnet run --project samples/ChatService/Trax.Samples.ChatService.Api

# Terminal 2 — React client (optional)
cd samples/ChatService/Trax.Samples.ChatService.Client
npm install && npm run dev
```

GraphQL IDE at `http://localhost:5210/trax/graphql`. React client at `http://localhost:5173`.

Authentication uses `X-Api-Key` header with three users: `alice-key`, `bob-key`, `charlie-key`.
The React client provides a user switcher dropdown — open multiple browser tabs to simulate different users chatting in real time.

**Quick walkthrough:**

```graphql
# 1. Create a room (as Alice)
mutation { dispatch { createChatRoom(input: { name: "General", userId: "alice", displayName: "Alice" }) { externalId output { chatRoomId name } } } }

# 2. Join the room (as Bob) — use the chatRoomId from step 1
mutation { dispatch { joinChatRoom(input: { chatRoomId: "<id>", userId: "bob", displayName: "Bob" }) { externalId output { joinedAt } } } }

# 3. Subscribe to real-time events (in a second tab)
subscription { onChatEvent(chatRoomId: "<id>") { eventType payload timestamp } }

# 4. Send a message — the subscription tab receives it
mutation { dispatch { sendMessage(input: { chatRoomId: "<id>", senderUserId: "alice", content: "Hello Bob!" }) { externalId output { messageId content sentAt } } } }

# 5. Query chat history
{ discover { getChatHistory(input: { chatRoomId: "<id>" }) { messages { senderDisplayName content sentAt } } } }
```
