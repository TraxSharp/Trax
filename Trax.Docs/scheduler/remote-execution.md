---
layout: default
title: Remote Execution
parent: Scheduling
nav_order: 9
---

# Remote Execution

By default, the scheduler schedules, dispatches, and executes trains all within the same process. Remote execution lets you separate **where trains are scheduled** from **where they run** — offloading execution to dedicated worker servers, AWS Lambda, ECS tasks, or any other compute.

> [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) | [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run) | [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers) | [AddTraxJobRunner / UseTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner) | [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers) | [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function) | [OverrideSubmitter](/docs/sdk-reference/scheduler-api/add-scheduler)

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
              ┌────────────────┼────────────────┬────────────────┐
              │                │                │                │
        Local Workers    Remote Workers    SQS Workers    Standalone Workers
        (same process)   (HTTP push)       (SQS + Lambda)  (separate process)
```

Two abstraction boundaries control where trains execute:

**For queued trains** (`queue*` mutations, scheduled jobs): The `IJobSubmitter` interface controls where the JobDispatcher sends work.

| Implementation | What it does |
|----------------|-------------|
| `PostgresJobSubmitter` | Inserts into `background_job` table (default when Postgres is configured) |
| `HttpJobSubmitter` | POSTs to a remote HTTP endpoint (used by `UseRemoteWorkers`) |
| `SqsJobSubmitter` | Sends to an SQS queue for Lambda consumption (used by [`UseSqsWorkers`](/docs/sdk-reference/scheduler-api/use-sqs-workers), requires `Trax.Scheduler.Sqs`) |
| `InMemoryJobSubmitter` | Runs inline, synchronously (automatic default when no database provider is configured) |
| Custom | Implement `IJobSubmitter` and register via `OverrideSubmitter()` |

**For run trains** (`run*` mutations, queries): The `IRunExecutor` interface controls where direct execution happens.

| Implementation | What it does |
|----------------|-------------|
| `LocalRunExecutor` | Executes in-process via `ITrainBus.RunAsync` (default) |
| `HttpRunExecutor` | POSTs to a remote HTTP endpoint, blocks until complete (used by [`UseRemoteRun`](/docs/sdk-reference/scheduler-api/use-remote-run)) |

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
        .ConfigureLocalWorkers(opts => opts.WorkerCount = 8)
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
        .UseRemoteWorkers(
            remote =>
            {
                remote.BaseUrl = "https://my-workers.example.com/trax/execute";
                remote.Timeout = TimeSpan.FromSeconds(60);
            },
            routing => routing.ForTrain<IMyTrain>())
        // Optional: also offload run* mutations to the remote endpoint
        .UseRemoteRun(remote =>
            remote.BaseUrl = "https://my-workers.example.com/trax/run"
        )
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
    )
);
```

**Remote side (ASP.NET Core host):**

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .UseBroadcaster(b => b.UseRabbitMq(rabbitMqConnectionString))
    )
    .AddMediator(typeof(MyTrain).Assembly)
);
builder.Services.AddTraxJobRunner();

var app = builder.Build();
app.UseTraxJobRunner("/trax/execute");  // queue path
app.UseTraxRunEndpoint("/trax/run");    // synchronous run path
app.Run();
```

The `UseBroadcaster()` call is essential for cross-process subscriptions — without it, the API process has no way to receive lifecycle events from the remote worker. See [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) for details.

**Remote side (AWS Lambda with `Trax.Runner.Lambda`):**

```csharp
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Trax.Runner.Lambda;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

public class Function : TraxLambdaFunction
{
    protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connString = configuration.GetConnectionString("TraxDatabase")!;
        var rabbitMqConnString = configuration.GetConnectionString("RabbitMQ")!;

        services.AddTrax(trax => trax
            .AddEffects(effects => effects
                .SkipMigrations() // Migrations run separately (API/CI) — skip for faster cold starts
                .UsePostgres(connString)
                .UseBroadcaster(b => b.UseRabbitMq(rabbitMqConnString)))
            .AddMediator(typeof(MyTrain).Assembly));
    }
}
```

The `TraxLambdaFunction` base class handles service provider lifecycle, request routing (`/trax/execute` and `/trax/run`), cancellation from Lambda's remaining time, and error handling. See the [TraxLambdaFunction API reference](/docs/sdk-reference/scheduler-api/trax-lambda-function) for details.

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

### Model 2b: SQS Workers (Queue-Based, AWS Lambda)

Like Remote Workers but with a durable SQS queue between the scheduler and workers. The scheduler sends `RemoteJobRequest` messages to SQS, and Lambda functions consume them. This adds guaranteed delivery, automatic retries, dead-letter queues, and backpressure that HTTP dispatch lacks.

Requires the `Trax.Scheduler.Sqs` package.

**Scheduler side:**

```csharp
using Trax.Scheduler.Sqs.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(assemblies)
    .AddScheduler(scheduler => scheduler
        .UseSqsWorkers(
            sqs => sqs.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs",
            routing => routing.ForTrain<IMyTrain>())
        // Optional: keep UseRemoteRun for synchronous mutations
        .UseRemoteRun(remote =>
            remote.BaseUrl = "https://my-runner.example.com/trax/run"
        )
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
    )
);
```

**Lambda consumer:**

```csharp
using Trax.Scheduler.Sqs.Lambda;

public class Function
{
    private static readonly IServiceProvider Services = BuildServiceProvider();
    private readonly SqsJobRunnerHandler _handler = new(Services);

    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        await _handler.HandleAsync(sqsEvent, context.CancellationToken);
    }
}
```

```
┌──── Scheduler Process ────┐         ┌──── SQS ────┐       ┌── Lambda ──────────────┐
│                            │         │              │       │                        │
│  ManifestManager           │         │  trax-jobs   │       │  SqsJobRunnerHandler   │
│  JobDispatcher             │         │  queue       │       │       │                │
│       │                    │   SQS   │              │  SQS  │       ▼                │
│       ▼                    │  Send   │              │ Event │  JobRunnerTrain        │
│  SqsJobSubmitter ──────────┼────────→│              │──────→│  └─→ Your Train        │
│                            │         │              │       │                        │
└────────────────────────────┘         └──────────────┘       └────────────────────────┘
                                                                       │
                                                                       ▼
                                                              Shared PostgreSQL
```

**When to use:**
- **AWS Lambda** — event-driven, auto-scaling, zero idle cost with durable message delivery
- **Guaranteed delivery** — SQS retries failed messages and dead-letters after max retries
- **Backpressure** — SQS buffers burst traffic; Lambda drains at a controlled rate
- **High volume** — thousands of concurrent jobs without overwhelming endpoints

**Sample:** The SQS transport is not yet production-ready. See the [Remote Workers](#model-2-remote-workers-push-based) model for the recommended Lambda deployment pattern using `TraxLambdaFunction`.

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

> **Tip:** `OverrideSubmitter` with `PostgresJobSubmitter` gives you a scheduler that only writes to the `background_job` table — no `LocalWorkerService` is started. By default (without `OverrideSubmitter`), local workers are started automatically when Postgres is configured, and you can run standalone workers alongside them for horizontal scaling.

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
| AWS Lambda with durability | **SQS Workers** — guaranteed delivery, retries, DLQ, auto-scaling |
| Google Cloud Run / Azure Functions | **Remote Workers** — push-based HTTP, matches serverless event model |
| Different hardware per train type | **Remote Workers** — route to GPU/high-memory endpoints |
| Just getting started | **Local Workers** — scale out later when you need to |

You can also mix models. For example, run local workers for fast trains and remote workers for expensive GPU trains — using per-train routing with `ForTrain<T>()`:

```csharp
.AddScheduler(scheduler => scheduler
    .ConfigureLocalWorkers(opts => opts.WorkerCount = 8)
    .UseRemoteWorkers(
        remote => remote.BaseUrl = "https://gpu-workers/trax/execute",
        routing => routing
            .ForTrain<IHeavyComputeTrain>()
            .ForTrain<IAiInferenceTrain>())
)
```

Trains not routed via `ForTrain<T>()` or `[TraxRemote]` execute locally.

## Authentication

Trax does not bake in any authentication mechanism. Both the scheduler and remote sides use standard ASP.NET patterns:

**Scheduler side** — configure the `HttpClient` used by `HttpJobSubmitter`:

```csharp
.UseRemoteWorkers(
    remote =>
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
    },
    routing => routing.ForTrain<IMyTrain>())
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

## Host Tracking

Every metadata record automatically captures which host executed the train — hostname, environment type (Lambda, ECS, Kubernetes, etc.), and instance ID. This works across all deployment models with zero configuration. You can also add custom labels (region, service, team) via the builder API.

See [Host Tracking](/docs/effect/host-tracking) for details on auto-detection, custom labels, and querying by host.

## Shared Requirements

Regardless of deployment model, every process that executes trains must:

1. **Reference the same train assemblies** — the train types are resolved by fully-qualified name
2. **Connect to the same Postgres database** — metadata, manifests, and state are shared
3. **Register the effect system** — `AddTrax()` with `UsePostgres()` and `AddMediator()`

## Failure Handling

When the JobDispatcher dispatches a job, the Metadata record is committed to the database **before** the job is submitted to the worker. This is necessary because the worker needs to read the Metadata. However, if the submission fails (network timeout, remote worker unreachable, throttling), the Metadata would be orphaned in `Pending` state.

Trax handles this with multiple layers of protection:

### 1. HTTP Retry with Exponential Backoff

`HttpJobSubmitter` and `HttpRunExecutor` automatically retry on transient HTTP status codes (429 Too Many Requests, 502 Bad Gateway, 503 Service Unavailable) with exponential backoff and jitter. This handles short-lived throttling — for example, when AWS Lambda returns 429 because reserved concurrency is exhausted.

| Option | Default | Description |
|--------|---------|-------------|
| `Retry.MaxRetries` | 5 | Maximum retry attempts before giving up |
| `Retry.BaseDelay` | 1 second | Starting delay, doubled on each attempt |
| `Retry.MaxDelay` | 30 seconds | Cap on exponential growth |

Configure via `RemoteWorkerOptions.Retry` or `RemoteRunOptions.Retry`:

```csharp
.UseRemoteWorkers(
    remote =>
    {
        remote.BaseUrl = "https://my-workers.example.com/trax/execute";
        remote.Retry.MaxRetries = 10;
        remote.Retry.BaseDelay = TimeSpan.FromSeconds(2);
        remote.Retry.MaxDelay = TimeSpan.FromSeconds(60);
    },
    routing => routing.ForTrain<IMyTrain>())
```

Set `MaxRetries = 0` to disable retries entirely.

If the server sends a `Retry-After` header (as Lambda does on 429), the helper respects it instead of using the computed backoff delay.

### 2. Dispatch Requeue

If the HTTP request still fails after exhausting retries, the work queue entry is automatically reset to `Queued` status so the next dispatcher cycle can try again. Each failed attempt:

- Marks the orphaned Metadata as `Failed` (immutable audit record)
- Increments `dispatch_attempts` on the work queue entry
- Resets `status` to `Queued`, clears `metadata_id` and `dispatched_at`
- On the next dispatch cycle, a **new** Metadata row is created

After `MaxDispatchAttempts` failures, the entry stays in `Dispatched` status and feeds into the dead letter pipeline.

```csharp
.AddScheduler(scheduler => scheduler
    .MaxDispatchAttempts(10) // default: 5
    // ...
)
```

Set `MaxDispatchAttempts(0)` to disable requeuing (immediate failure, matching pre-1.2.0 behavior).

### 3. Stale Pending Reaper

The ManifestManager runs a `ReapStalePendingMetadataJunction` on every polling cycle. Any Metadata that has been in `Pending` state longer than `StalePendingTimeout` (default: 20 minutes) is automatically marked as `Failed`. This catches edge cases where the remote worker received the job but crashed before updating the Metadata.

```csharp
.AddScheduler(scheduler => scheduler
    .StalePendingTimeout(TimeSpan.FromMinutes(10))
    // ...
)
```

Or at runtime via the Dashboard under **Server Settings > Job Settings > Stale Pending Timeout**.

### 4. Dead-Lettering

After `MaxRetries` failed **executions** (distinct from dispatch attempts), the ManifestManager creates a `DeadLetter` record and marks the manifest as `AwaitingIntervention`. Dead letters can be resolved via the Dashboard or programmatically.

Failed metadata feeds into the normal retry pipeline — if the manifest has retries remaining, the ManifestManager will create a new work queue entry on the next cycle.

### Tuning for Throttled Environments

When deploying to capacity-limited backends (e.g., AWS Lambda with reserved concurrency), align these settings:

| Setting | Recommendation |
|---------|---------------|
| `MaxConcurrentDispatch` | Match or stay below the backend's concurrency limit |
| `MaxActiveJobs` | Match the backend's concurrency limit to prevent dispatch overwhelming |
| `Retry.MaxRetries` | 5-10 for throttle-heavy environments |
| `MaxDispatchAttempts` | 5-10 to cover longer outages |

### Structured Error Propagation

When a train fails on a remote worker, Trax preserves the full exception context across the HTTP boundary. Both endpoints (`/trax/execute` and `/trax/run`) return structured error responses with:

| Field | Description |
|-------|-------------|
| `IsError` | Whether the execution failed |
| `ErrorMessage` | The error message |
| `ExceptionType` | The .NET exception type name (e.g., `"InvalidOperationException"`) |
| `FailureJunction` | The train junction where the failure occurred (extracted from `TrainExceptionData`) |
| `StackTrace` | The remote stack trace |

On the API side, `HttpJobSubmitter` and `HttpRunExecutor` read the response body and reconstruct a `TrainException` with the structured data intact. This ensures that `Metadata.AddException()` on the API side correctly parses the failure into `FailureException`, `FailureJunction`, `FailureReason`, and `StackTrace` — the same fields you'd see for a locally-executed train failure.

```
Runner Process                         API Process
─────────────────                      ───────────────────
Train fails with exception
    │
    ▼
TraxRequestHandler catches exception
Extracts: Type, Junction, Message, Stack
    │
    ▼
RemoteRunResponse / RemoteJobResponse
(structured error fields)
    │
    ├───── HTTP 200 + JSON body ──────→ HttpRunExecutor / HttpJobSubmitter
                                        reads response body
                                            │
                                            ▼
                                        Reconstructs TrainException
                                        with TrainExceptionData JSON
                                            │
                                            ▼
                                        Metadata.AddException() parses
                                        into structured failure fields
```

If the HTTP call itself fails (network error, infrastructure 5xx before reaching the endpoint), the error body is read and included in the exception message for debugging — you'll see the HTTP status code and the response body rather than a generic "500 Internal Server Error".

### Debugging Remote Failures

When a remote job fails, check these in order:

1. **Metadata table** — `SELECT failure_exception, failure_junction, failure_reason, stack_trace FROM trax.metadata WHERE id = <id>`. These fields are populated from the structured error response.
2. **Log table** — `SELECT * FROM trax.log WHERE metadata_id = <id> ORDER BY id`. If `AddDataContextLogging()` is enabled on the runner, junction-level logs are persisted.
3. **Stale pending check** — If `failure_exception = 'StalePendingTimeout'`, the runner never started executing. Check runner health, network connectivity, and deployment status.

## Limitations

- **Cancellation is process-local.** The `ICancellationRegistry` is in-memory. Dashboard "Cancel" only cancels trains running on the same process as the dashboard. Remote trains cannot be cancelled via the dashboard in v1.
- **Type resolution requires shared assemblies.** The remote process must reference the same NuGet packages and assemblies that define your train types. Types are resolved by fully-qualified name from loaded assemblies.

## See Also

- [Job Submission](/docs/scheduler/job-submission) — architecture of the job submission pipeline
- [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers) — API reference for local worker configuration
- [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) — API reference for remote workers (HTTP)
- [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers) — API reference for SQS workers (Lambda)
- [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run) — API reference for remote run execution
- [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner) — API reference for remote receiver setup
- [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) — API reference for standalone worker setup
