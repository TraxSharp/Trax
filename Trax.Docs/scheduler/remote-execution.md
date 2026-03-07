---
layout: default
title: Remote Execution
parent: Scheduling
nav_order: 9
---

# Remote Execution

By default, the scheduler schedules, dispatches, and executes trains all within the same process. Remote execution lets you separate **where trains are scheduled** from **where they run** — offloading execution to dedicated worker servers, AWS Lambda, ECS tasks, or any other compute.

## Key Concept

Postgres is always the source of truth. Every deployment model — local, remote, or standalone — connects to the same Postgres database for metadata, manifests, and state. The only thing that changes is **where the train code runs**.

```
┌──────────────────────────────────────────────────────────────────────────┐
│                         Shared PostgreSQL                               │
│                                                                         │
│  trax.manifest    trax.metadata    trax.work_queue    trax.dead_letter  │
│  (schedules)      (job state)      (dispatch queue)   (failed jobs)     │
└──────────────────────────────┬───────────────────────────────────────────┘
                               │
              ┌────────────────┼────────────────┐
              │                │                │
        Local Workers    Remote Workers    Standalone Workers
        (same process)   (HTTP push)       (separate process)
```

Two abstraction boundaries control where trains execute:

**For queued trains** (`queue*` mutations, scheduled jobs): The `IJobSubmitter` interface controls where the JobDispatcher sends work.

| Implementation | What it does |
|----------------|-------------|
| `PostgresJobSubmitter` | Inserts into `background_job` table (used by `UseLocalWorkers`) |
| `HttpJobSubmitter` | POSTs to a remote HTTP endpoint (used by `UseRemoteWorkers`) |
| `InMemoryJobSubmitter` | Runs inline, synchronously (used by `UseInMemoryWorkers`) |
| Custom | Implement `IJobSubmitter` and register via `OverrideSubmitter()` |

**For run trains** (`run*` mutations, queries): The `IRunExecutor` interface controls where direct execution happens.

| Implementation | What it does |
|----------------|-------------|
| `LocalRunExecutor` | Executes in-process via `ITrainBus.RunAsync` (default) |
| `HttpRunExecutor` | POSTs to a remote HTTP endpoint, blocks until complete (used by [`UseRemoteRun`]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-remote-run.md %})) |

## Deployment Models

### Model 1: Local Workers

Everything runs on one process. This is the default and simplest setup.

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(assemblies)
    .AddScheduler(scheduler => scheduler
        .UseLocalWorkers(opts => opts.WorkerCount = 8)
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
    )
);
```

```
┌─────────────────── Single Process ───────────────────┐
│                                                       │
│  ManifestManager ──→ WorkQueue ──→ JobDispatcher      │
│                                        │              │
│                                        ▼              │
│                              PostgresJobSubmitter      │
│                                        │              │
│                                        ▼              │
│                              background_job table      │
│                                        │              │
│                                        ▼              │
│                              LocalWorkerService       │
│                              (N worker tasks)         │
│                                        │              │
│                                        ▼              │
│                              JobRunnerTrain           │
│                              └─→ Your Train           │
└───────────────────────────────────────────────────────┘
```

**When to use:** Most applications. Simple, no network hops, easy to debug. Start here and scale out only when you need to.

### Model 2: Remote Workers (Push-Based)

The scheduler dispatches jobs via HTTP POST to a remote endpoint. The remote process receives the request and runs the train.

**Scheduler side:**

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(assemblies)
    .AddScheduler(scheduler => scheduler
        .UseRemoteWorkers(remote =>
        {
            remote.BaseUrl = "https://my-workers.example.com/trax/execute";
            remote.Timeout = TimeSpan.FromSeconds(60);
        })
        // Optional: also offload run* mutations to the remote endpoint
        .UseRemoteRun(remote =>
            remote.BaseUrl = "https://my-workers.example.com/trax/run"
        )
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
    )
);
```

**Remote side (any ASP.NET Core host):**

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(MyTrain).Assembly)
);
builder.Services.AddTraxJobRunner();

var app = builder.Build();
app.UseTraxJobRunner("/trax/execute");  // queue path
app.UseTraxRunEndpoint("/trax/run");    // synchronous run path
app.Run();
```

```
┌──── Scheduler Process ────┐         ┌──── Remote Process ────────────┐
│                            │         │                                │
│  ManifestManager           │         │  POST /trax/execute            │
│  JobDispatcher             │         │       │                        │
│       │                    │         │       ▼                        │
│       ▼                    │  HTTP   │  JobRunnerTrain                │
│  HttpJobSubmitter ─────────┼────────→│  └─→ Your Train               │
│                            │  POST   │                                │
└────────────────────────────┘         └────────────────────────────────┘
                                                │
                                                ▼
                                       Shared PostgreSQL
```

**When to use:**
- **Serverless compute** (AWS Lambda, Google Cloud Run, Azure Functions) — trains only run when invoked, zero idle cost
- **Isolation** — trains run in a separate security boundary or VPC
- **Heterogeneous compute** — different train types need different hardware (GPU, high memory)
- **Scaling** — the remote endpoint can auto-scale independently of the scheduler

**Sample:** See `Trax.Samples.ContentShield.Api` and `Trax.Samples.ContentShield.Runner` in the `samples/EphemeralWorkers/` directory of the Trax.Samples repository. The API serves GraphQL and dispatches queued mutations to the Runner via HTTP — no `background_job` table, no DB polling. The Runner uses `UseBroadcaster` with RabbitMQ so GraphQL subscriptions on the API are notified when queued trains complete.

### Model 3: Standalone Workers (Poll-Based)

A separate, always-on process polls the `background_job` table and runs trains. No scheduler logic — just execution.

**Scheduler side** (scheduling only — no local execution):

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(assemblies)
    .AddScheduler(scheduler => scheduler
        // Register PostgresJobSubmitter without starting local workers.
        // Jobs are written to background_job and picked up by the worker process.
        .OverrideSubmitter(s => s.AddScoped<IJobSubmitter, PostgresJobSubmitter>())
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
    )
);
```

> **Tip:** `OverrideSubmitter` with `PostgresJobSubmitter` gives you a scheduler that only writes to the `background_job` table — no `LocalWorkerService` is started. If you want the scheduler to also execute some jobs locally, use `UseLocalWorkers()` instead and run standalone workers alongside it for horizontal scaling.

**Standalone worker process:**

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(MyTrain).Assembly)
);
builder.Services.AddTraxWorker(opts => opts.WorkerCount = 4);

var app = builder.Build();
app.Run();
```

```
┌──── Scheduler ────────────┐         ┌──── Standalone Worker ─────────┐
│                            │         │                                │
│  ManifestManager           │         │  LocalWorkerService            │
│  JobDispatcher             │         │  (4 worker tasks)              │
│       │                    │         │       │                        │
│       ▼                    │         │       ▼                        │
│  PostgresJobSubmitter      │         │  SELECT ... FOR UPDATE         │
│       │                    │         │  SKIP LOCKED                   │
│       ▼                    │         │       │                        │
│  background_job ───────────┼─────────┼──→ JobRunnerTrain              │
│  table                     │  same   │       └─→ Your Train           │
└────────────────────────────┘  DB     └────────────────────────────────┘
```

**When to use:**
- **Separate servers** — dedicated worker machines with different specs
- **Horizontal scaling** — run multiple worker processes, each polling the same table (PostgreSQL `SKIP LOCKED` prevents duplicates)
- **Process isolation** — scheduler crash doesn't kill in-flight trains
- **Kubernetes/ECS** — deploy workers as a separate service with independent scaling

**Sample:** See `Trax.Samples.EnergyHub.Hub` and `Trax.Samples.EnergyHub.Worker` in the `samples/DistributedWorkers/` directory of the Trax.Samples repository for a working example. The Hub combines GraphQL API, scheduler, and dashboard in one process while offloading all train execution to the Worker.

## Which Model Should I Use?

| Scenario | Recommended Model |
|----------|-------------------|
| Single-server deployment | **Local Workers** — simplest setup, no network overhead |
| Separate worker servers (always running) | **Standalone Workers** — poll-based, no HTTP layer needed |
| AWS Lambda / Cloud Run / Azure Functions | **Remote Workers** — push-based, matches serverless event model |
| Different hardware per train type | **Remote Workers** — route to GPU/high-memory endpoints |
| Just getting started | **Local Workers** — scale out later when you need to |

You can also mix models. For example, run local workers for fast trains and remote workers for expensive GPU trains — using `OverrideSubmitter()` to route based on train type.

## Authentication

Trax does not bake in any authentication mechanism. Both the scheduler and remote sides use standard ASP.NET patterns:

**Scheduler side** — configure the `HttpClient` used by `HttpJobSubmitter`:

```csharp
.UseRemoteWorkers(remote =>
{
    remote.BaseUrl = "https://my-workers.example.com/trax/execute";

    // Bearer token
    remote.ConfigureHttpClient = client =>
        client.DefaultRequestHeaders.Add("Authorization", "Bearer my-token");

    // Or API key
    remote.ConfigureHttpClient = client =>
        client.DefaultRequestHeaders.Add("X-Api-Key", "my-key");

    // Or any custom header your endpoint expects
    remote.ConfigureHttpClient = client =>
        client.DefaultRequestHeaders.Add("X-Custom-Header", "value");
})
```

**Remote side** — use ASP.NET middleware:

```csharp
var app = builder.Build();

// Your choice of auth middleware:
app.UseAuthentication();
app.UseAuthorization();

app.UseTraxJobRunner("/trax/execute");
app.Run();
```

Or restrict the endpoint directly:

```csharp
app.UseTraxJobRunner("/trax/execute").RequireAuthorization();
```

This keeps Trax focused on scheduling and execution while letting you use whatever auth strategy your infrastructure requires — API keys, JWT tokens, mTLS, IAM roles, or nothing at all.

## Shared Requirements

Regardless of deployment model, every process that executes trains must:

1. **Reference the same train assemblies** — the train types are resolved by fully-qualified name
2. **Connect to the same Postgres database** — metadata, manifests, and state are shared
3. **Register the effect system** — `AddTrax()` with `UsePostgres()` and `AddMediator()`

## Failure Handling

When the JobDispatcher dispatches a job, the Metadata record is committed to the database **before** the job is submitted to the worker. This is necessary because the worker needs to read the Metadata. However, if the submission fails (network timeout, remote worker unreachable, HTTP 5xx), the Metadata would be orphaned in `Pending` state.

Trax handles this with two layers of protection:

1. **Immediate failure handling** — If `IJobSubmitter.EnqueueAsync()` throws, the DispatchJobsStep immediately marks the Metadata as `Failed` with the exception details. This covers the majority of dispatch failures.

2. **Stale pending reaper** — The ManifestManager runs a `ReapStalePendingMetadataStep` on every polling cycle. Any Metadata that has been in `Pending` state longer than `StalePendingTimeout` (default: 20 minutes) is automatically marked as `Failed`. This catches edge cases where immediate failure handling also fails, or where the remote worker received the job but crashed before updating the Metadata.

Configure the timeout via the builder API:

```csharp
.AddScheduler(scheduler => scheduler
    .StalePendingTimeout(TimeSpan.FromMinutes(10))
    // ...
)
```

Or at runtime via the Dashboard under **Server Settings > Job Settings > Stale Pending Timeout**.

Failed metadata feeds into the normal retry pipeline — if the manifest has retries remaining, the ManifestManager will create a new work queue entry on the next cycle.

## Limitations

- **Cancellation is process-local.** The `ICancellationRegistry` is in-memory. Dashboard "Cancel" only cancels trains running on the same process as the dashboard. Remote trains cannot be cancelled via the dashboard in v1.
- **Type resolution requires shared assemblies.** The remote process must reference the same NuGet packages and assemblies that define your train types. Types are resolved by fully-qualified name from loaded assemblies.

## See Also

- [Job Submission]({{ site.baseurl }}{% link scheduler/job-submission.md %}) — architecture of the job submission pipeline
- [UseLocalWorkers]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-local-workers.md %}) — API reference for local workers
- [UseRemoteWorkers]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-remote-workers.md %}) — API reference for remote workers (queue path)
- [UseRemoteRun]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-remote-run.md %}) — API reference for remote run execution
- [AddTraxJobRunner]({{ site.baseurl }}{% link sdk-reference/scheduler-api/add-trax-job-runner.md %}) — API reference for remote receiver setup
- [AddTraxWorker]({{ site.baseurl }}{% link sdk-reference/scheduler-api/add-trax-worker.md %}) — API reference for standalone worker setup
