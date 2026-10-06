---
layout: default
title: Samples & Deployment
description: Every Trax sample, the feature each one proves and the test that proves it, the trains-library and data-layer patterns, and the deployment models.
nav_order: 10
has_children: true
section: Guides
---

# Samples & Deployment Patterns

[Trax.Samples](https://github.com/TraxSharp/Trax.Samples) holds one sample per major Trax feature. Each is a complete,
runnable application that shows that feature and as little else as possible, and each has an end-to-end test suite
that runs against the real host, so what a page here tells you to copy is something CI has seen work. Start from the
sample for the feature you need, then read its page: it lists the packages, the full `Program.cs`, what refuses
startup and why, and a "Try it" walkthrough whose commands were run as written.

Features without a sample of their own are proven by a test in their own repository; the
[feature-coverage table](https://github.com/TraxSharp/Trax.Samples#feature-coverage) lists every major feature and
the test class that proves it.

Starting a new server rather than learning one feature? The [project templates](/docs/reference/templates)
(`trax-hub`, `trax-api`, `trax-scheduler`) scaffold a runnable host with a README and a test project:
`dotnet new install Trax.Samples.Templates && dotnet new trax-hub -n MyApp`.

## Pick a Sample

| Sample | The feature it proves | Run (from `Trax.Samples/`) | Port |
|---|---|---|---|
| [Scheduling](/docs/samples/scheduling) | Interval, cron, one-off, dependent and dormant manifests; retries with backoff, dead letters and their requeue over GraphQL | `dotnet run --project samples/Scheduling/Trax.Samples.Scheduling.Host` | 5230 |
| [Auth](/docs/samples/auth) | Securing a Trax server end to end: API keys and JWT side by side, `[TraxAuthorize]` roles and policies, `GateOperations`, scheme-qualified principal ids, the audit trail | `dotnet run --project samples/Auth/Trax.Samples.Auth` | 5220 |
| [Recovery](/docs/samples/recovery) | Train decisions, a manifest retry that replays them instead of asking the model again, and live junction events on a React page | `dotnet run --project samples/Recovery/Trax.Samples.Recovery.Api` | 5260 |
| [Chat Service](/docs/samples/chat-service) | GraphQL subscriptions over WebSocket: a lifecycle hook feeding a custom `onChatEvent` field that only a room's participants may join | `dotnet run --project samples/ChatService/Trax.Samples.ChatService.Api` | 5210 |
| [GraphQL Client](/docs/samples/graphql-client) | Keyed Trax GraphQL clients calling two Trax servers, each request validated against its server's schema before it is sent | `dotnet run --project samples/GraphQLClient/Trax.Samples.GraphQLClient.Gateway` | 5310-5314 |
| [Persisted Operations](/docs/samples/persisted-operations) | An API that runs only stored documents: gated management mutations, a hot-fix by id, the shape-diff guardrail | `dotnet run --project samples/PersistedOperations/Trax.Samples.PersistedOperations.Api` | 5240 |
| [Bookworm](/docs/samples/bookworm) | Cross-schema GraphQL over two domain contexts, owner-scoped rows, and the architecture guards adopted by a consumer | `dotnet run --project samples/Bookworm/Trax.Samples.Bookworm.Api` | 5250 |
| [State Machine](/docs/samples/state-machine) | Snapshot state machines behind the `stateMachine` mutations: an exactly-once charge, a forward migration, a server-checked total | `dotnet run --project samples/StateMachine/Trax.Samples.StateMachine.Api` | 5280 |
| [Energy Hub](/docs/samples/energy-hub) | A hub that schedules and serves GraphQL but runs none of its jobs, and standalone workers that do, with events home over RabbitMQ | hub and worker, see the page | 5202-5203 |
| [Content Shield](/docs/samples/content-shield) | An API that executes nothing: queued and synchronous runs go, signed, to a Lambda-style runner | runner and API, see the page | 5204-5205 |
| [SignalR Broadcaster](/docs/samples/signalr-broadcaster) | Live train events in a browser through the SignalR sink, a hub only signed-in operators may join, a projected failure reason | `dotnet run --project samples/SignalRBroadcaster/Trax.Samples.SignalRBroadcaster` | 5270 |

The ports never collide, so any set of samples can run side by side. The React clients (Chat Service, Recovery,
State Machine) use Vite's dev server on 5173, the one origin their hosts allow; run one at a time. A second
client refuses to start rather than move to another port.

## Running the Samples

Most samples need PostgreSQL, and the distributed ones RabbitMQ. From the `Trax.Samples/` directory:

```bash
docker compose up -d
```

The compose file publishes Postgres (`trax` / `trax123`) and RabbitMQ (`trax` / `trax123`) on
`127.0.0.1` only, because their passwords are written in the file. Set `TRAX_PG_PORT` to move Postgres to another
host port when something else holds 5432; the samples themselves read their connection string from their own
`appsettings.json`, so change it there too. If you copy the compose file to a server, keep the `127.0.0.1:` prefix and
replace the passwords.

Every Postgres sample has a database of its own (`trax_auth`, `trax_bookworm`, `trax_contentshield`, `trax_energyhub`,
`trax_persisted_operations`, `trax_recovery`, `trax_scheduling`, `trax_statemachine`), because a scheduler polls every
manifest and job in its database and would fail another sample's. `docker compose up -d` creates them on a fresh
volume. Its init script runs only once, so on a volume from before a database was added, create the database yourself:
`docker exec trax_database createdb -U trax trax_scheduling`.

The samples' demo API keys and JWT signing keys are published in this repository, so each sample registers them only
in Development, and so does its dashboard. `dotnet run` starts in Development through the project's
`Properties/launchSettings.json`; started any other way, a sample accepts none of them and serves no dashboard. Every
demo API key contains `do-not-use-in-production`, which Trax.Api refuses to start with outside Development.

The end-to-end suites run with `dotnet test` from `Trax.Samples/`. They use Postgres on port 5432 by default; set
`TRAX_TEST_PG_PORT` to point them elsewhere. A suite whose database is missing fails rather than skipping.

## The Trains Library Pattern

Every sample follows the same two-layer split:

```
MyApp/                          ← library (class library, not executable)
  Trains/
    Feature1/
      IFeature1Train.cs
      Feature1Train.cs
      Feature1Input.cs
      Junctions/
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

- **Trains** - `ServiceTrain<TIn, TOut>` implementations with their junctions
- **Interfaces** - `IServiceTrain<TIn, TOut>` contracts for each train
- **Inputs and outputs** - POCOs that define each train's data contract
- **ManifestNames** - string constants for scheduler manifest IDs
- **Domain types** - any shared models, enums, or utilities

The library references `Trax.Effect`, `Trax.Mediator`, and `Trax.Scheduler` (or whatever layers your trains need), but it does **not** reference infrastructure packages like `Trax.Dashboard`, `Trax.Api.GraphQL`, or the effect providers. It has no `Program.cs` and no `appsettings.json`.

```xml
<!-- Library .csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Trax.Effect" Version="1.59.0" />
    <PackageReference Include="Trax.Effect.Data.Postgres" Version="1.59.0" />
    <PackageReference Include="Trax.Mediator" Version="1.24.1" />
    <PackageReference Include="Trax.Scheduler" Version="1.36.1" />
  </ItemGroup>
</Project>
```

### The Executables

Each executable is a `Microsoft.NET.Sdk.Web` project with a `ProjectReference` to the library. Its `Program.cs` calls `AddTrax()` and configures whichever capabilities this process needs - scheduling, dashboard, GraphQL, worker polling, or any combination.

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
    <PackageReference Include="Trax.Effect.Provider.Json" Version="1.59.0" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" Version="1.59.0" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Progress" Version="1.59.0" />
    <PackageReference Include="Trax.Dashboard" Version="1.18.0" />
  </ItemGroup>
</Project>
```

The versions are the releases these docs are checked against. Pin exact versions, ideally once for
the solution in `Directory.Packages.props`: a floating `Version="1.*"` restores whatever was
published last, and a Trax minor release can change an API your trains call.

The key line in `Program.cs` is the assembly scan - it points at the library so the train bus discovers all your trains:

```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        // ... add whatever this executable needs
    )
    .AddMediator(typeof(ManifestNames).Assembly, ...)
);
```

Different executables add different capabilities on top of the same trains. That's the entire pattern.

## The Data Layer Pattern

Samples that own relational data follow a strict rule: **one project, one PostgreSQL schema, one DbContext** (1:1:1). The `Bookworm` sample is the reference implementation, with a `catalog` domain (books, authors) and a `lending` domain (members, loans) in separate projects, each owning its own schema.

A domain context derives a shared base, `DomainDataContext<TSelf>`, which applies the default schema (on PostgreSQL), a UTC datetime converter, and seals `OnModelCreating` so the conventions cannot be skipped:

```csharp
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DomainDataContext<CatalogDbContext>(options), ICatalogDbContext
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Author> Authors => Set<Author>();

    protected override string Schema => CatalogSchema.Name; // "catalog"

    protected override void ConfigureModel(ModelBuilder modelBuilder) { /* keys, indexes, relationships */ }
}
```

Each context ships a companion `I{Name}DbContext` interface, in its own `I{Name}DbContext.cs` file next to the context; application code (junctions, services) depends on the interface, never the concrete type. Registration goes through `AddDomainDataContext<TInterface, TContext>` (from Trax.Effect.Data), which uses a pooled context factory plus a scoped resolver. A pooled context cannot take the caller, so a context with owner-scoped rows (Bookworm's `lending`) is registered by hand instead; the [Bookworm sample](/docs/samples/bookworm) shows how.

### Crossing schema boundaries

A domain never references another domain. A loan's reference to a catalog book is a plain integer column, not an EF navigation:

```csharp
[Table("loans")]
public class Loan
{
    [Column("book_id")]
    public int BookId { get; set; } // points at catalog.books; resolved cross-schema in GraphQL
}
```

The GraphQL `loan.book` field is resolved by a **batched cross-schema data loader** that lives in its own project (`Bookworm.CrossSchema`), the one place allowed to reference more than one domain context. EF Core cannot JOIN across two contexts, so the loader collects every requested book id in a request and issues a single `WHERE id IN (...)` against the catalog context, avoiding an N+1:

```csharp
[ExtendObjectType(typeof(Loan))]
public sealed class LoanToBookEdge
{
    public async Task<Book?> GetBook(
        [Parent] Loan loan,
        CrossSchemaLoader<CatalogDbContext, Book> books,
        CancellationToken ct
    ) => await books.LoadAsync(loan.BookId, ct);
}
```

Edges are declared in a single manifest (`CrossSchemaEdges.All`) that meta-tests reflect over to verify each edge has a real integer foreign key, a target owned by the declared context, and a registered loader.

When a context genuinely needs to read another schema's table at the EF level (rather than only at the GraphQL layer), the foreign entity exposes a static `OnCrossSchemaModelCreating(ModelBuilder, string schema)` that pins it to the foreign schema and **ignores every navigation**, so EF Core never walks the foreign model graph into the consuming context. The entity is exposed there through a scalar-only `I{Entity}Reference` interface via explicit interface implementation, which keeps it out of GraphQL discovery (discovery only enumerates public `DbSet<T>` properties), so the owning domain stays the single GraphQL owner.

### Guarding the pattern

The conventions above are enforced by meta-tests so they survive future changes. `Trax.Samples.Tests.Meta` scans source on disk (every domain context derives the base and has a companion interface, every `OnCrossSchemaModelCreating` has the standard signature, cross-schema edge resolvers live only in a `*.CrossSchema` project and always go through the loader). `Trax.Samples.Tests.Reflection` references the built assemblies and checks what only the EF model and type graph can prove (each context owns a distinct non-null schema, every edge in the manifest maps to a real foreign key and a registered loader, every train has its `I{Name}Train` interface). Allowlists carry a justification per entry and fail when they go stale.

## Deployment Models

The same trains library can be wrapped by different executables. The samples cover five topologies.

### Model 1: One Host Does Everything

**Samples:** [Scheduling](/docs/samples/scheduling), [Recovery](/docs/samples/recovery)

One ASP.NET process schedules trains, runs them on the built-in local workers (the default whenever the scheduler
has a database provider), serves GraphQL, and hosts the dashboard in Development. The Recovery sample adds
subscriptions on the same host: its page listens to `onJunctionEvent` for live progress.

- **Host:** `AddScheduler()` + `AddTraxGraphQL()` + `AddTraxDashboard()`

Good for: most applications, until execution needs to scale apart from the API.

### Model 2: Separate API + Scheduler

**Sample:** none. Two processes share the trains library and the Postgres database: the scheduler runs background
work and hosts the dashboard; the API serves GraphQL, runs lightweight trains inline, and queues heavy ones. Because
they are separate processes, both call `UseBroadcaster(b => b.UseRabbitMq(...))`, so the API's subscriptions see the
trains the scheduler ran.

- **Scheduler:** `AddScheduler()` + `AddTraxDashboard()` + `UseBroadcaster()`
- **API:** `AddTraxGraphQL()` + `UseBroadcaster()`. Queueing needs a job submitter, so give the API
  `AddScheduler(s => s.OverrideSubmitter(...))` (it writes jobs, the scheduler process runs them). A host that calls
  `ExposeOperationMutations()` with no `IJobSubmitter` registered refuses to start, because `runTrain` hands every run
  to one.

Good for: an API that must stay responsive while background jobs run elsewhere.

### Model 3: Hub + Distributed Workers

**Sample:** [Energy Hub](/docs/samples/energy-hub)

The hub schedules and serves the API but executes nothing: `OverrideSubmitter(s => s.AddScoped<IJobSubmitter,
PostgresJobSubmitter>())` writes jobs to `background_job` and starts no local workers (without the override, a
scheduler on Postgres runs trains too). Separate worker processes (`AddTraxWorker()`) claim jobs with
`FOR UPDATE SKIP LOCKED` and scale horizontally. Workers publish lifecycle events over RabbitMQ, so the hub's GraphQL
subscriptions see remote completions.

Good for: high throughput, and scaling execution independently from scheduling.

### Model 4: Ephemeral Workers (Serverless)

**Sample:** [Content Shield](/docs/samples/content-shield)

All work is triggered by GraphQL. The API runs no trains: `UseRemoteWorkers()` POSTs queued jobs to the runner and
`UseRemoteRun()` sends every synchronous run there too, **queries included**. The runner is a `TraxLambdaFunction`:
in production an AWS Lambda function, in development `RunLocalAsync()` serving `/trax/execute` and `/trax/run`. Both
sides share a signing key (the runner's [authorization posture](/docs/scheduler/remote-execution#authorization-posture));
outside Development a missing key stops startup. No `background_job` table is involved. The runner publishes lifecycle
events over RabbitMQ, so the API's subscriptions see completions.

Good for: serverless deployments, on-demand workloads with zero idle cost.

### Model 5: Single Server with Domain Subscriptions

**Sample:** [Chat Service](/docs/samples/chat-service)

No scheduler and no workers. A custom `ITrainLifecycleHook`, registered through its factory, fires for every train
that completes (custom hooks do not depend on `[TraxBroadcast]`) and publishes a domain event to a custom
`onChatEvent` field that extends `LifecycleSubscriptions`, Trax's subscription root. Sockets authenticate in
`connection_init`, and the field admits only a room's participants.

Good for: chat, collaboration and notification feeds driven by train results.

## Comparing the Models

| Capability | One host | Separate API | Distributed | Ephemeral | Single server |
|---|---|---|---|---|---|
| Processes | 1 | 2 | 2+ | 2 | 1 |
| Scheduler | In-process | Scheduler process | Hub (scheduling only) | API (dispatch only) | None |
| Execution | Local workers | Scheduler process | Workers (polling) | Runner (HTTP push) | Inline |
| API | In-process | Separate process | Hub | API | In-process |
| Dashboard (Development) | In-process | In scheduler | In hub | In API | None |
| Job table | `background_job` | `background_job` | `background_job` | None (direct HTTP) | None |
| Horizontal scaling | No | No | Workers | Runner auto-scales | No |
| Subscriptions | Built-in, junction events | Over RabbitMQ | Over RabbitMQ | Over RabbitMQ | Custom field from a lifecycle hook |

In every model the trains library is the same; only the `Program.cs` files differ.

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) | [AddJson](/docs/sdk-reference/configuration/add-json-effect) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionProgress](/docs/sdk-reference/configuration/add-junction-progress) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook) | [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers) | [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) | [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run) | [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner) | [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard) | [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [UseTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [TraxBroadcast](/docs/sdk-reference/graphql-api/trax-broadcast-attribute) | [TraxQueryModel](/docs/sdk-reference/graphql-api/query-models)
