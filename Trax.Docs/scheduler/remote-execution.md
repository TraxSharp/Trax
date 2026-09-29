---
layout: default
title: Remote Execution
parent: Scheduling
nav_order: 9
---

# Remote Execution

By default, the scheduler schedules, dispatches, and executes trains all within the same process. Remote execution lets you separate **where trains are scheduled** from **where they run**, offloading execution to dedicated worker servers, AWS Lambda, ECS tasks, or any other compute.

## Key Concept

Postgres is always the source of truth. Every deployment model, local, remote, or standalone, connects to the same Postgres database for metadata, manifests, and state. The only thing that changes is **where the train code runs**.

```
┌──────────────────────────────────────────────────────────────────────────┐
│                         Shared PostgreSQL                               │
│                                                                         │
│  trax.manifest    trax.metadata    trax.work_queue    trax.dead_letter  │
│  (schedules)      (job state)      (dispatch queue)   (failed jobs)     │
└──────────────────────────────┬───────────────────────────────────────────┘
                               │
              ┌──────────────┼───────────────┬────────────────┬────────────────┐
              │              │               │                │                │
        Local Workers  Remote Workers  Lambda Workers  SQS Workers    Standalone Workers
        (same process) (HTTP push)     (direct SDK)    (SQS + Lambda)  (separate process)
```

Two abstraction boundaries control where trains execute:

**For queued trains** (`queue*` mutations, scheduled jobs): The `IJobSubmitter` interface controls where the JobDispatcher sends work.

| Implementation | What it does |
|----------------|-------------|
| `PostgresJobSubmitter` | Inserts into `background_job` table (default when Postgres is configured) |
| HTTP, from [`UseRemoteWorkers`](/docs/sdk-reference/scheduler-api/use-remote-workers) | POSTs to a remote HTTP endpoint |
| SQS, from [`UseSqsWorkers`](/docs/sdk-reference/scheduler-api/use-sqs-workers) | Sends to an SQS queue for Lambda consumption (requires `Trax.Scheduler.Sqs`) |
| Lambda, from [`UseLambdaWorkers`](/docs/sdk-reference/scheduler-api/use-lambda-workers) | Invokes an AWS Lambda function directly via SDK (requires `Trax.Scheduler.Lambda`) |
| In-memory | Runs inline, synchronously (automatic default when no database provider is configured) |
| Custom | Implement `IJobSubmitter` and register via `OverrideSubmitter()` |

Only `PostgresJobSubmitter` is a public type. The HTTP, SQS, Lambda and in-memory submitters are internal: you select them with the builder method, and the JobDispatcher resolves them for the trains routed to them.

**For run trains** (`run*` mutations, queries): The `IRunExecutor` interface controls where direct execution happens.

| Implementation | What it does |
|----------------|-------------|
| `LocalRunExecutor` | Executes in-process via `ITrainBus.RunAsync` (default) |
| HTTP, from [`UseRemoteRun`](/docs/sdk-reference/scheduler-api/use-remote-run) | POSTs to a remote HTTP endpoint, blocks until complete |
| Lambda, from [`UseLambdaRun`](/docs/sdk-reference/scheduler-api/use-lambda-run) | Invokes an AWS Lambda function directly via SDK, blocks until complete (requires `Trax.Scheduler.Lambda`) |

The HTTP and Lambda executors are internal. Each builder method replaces the `IRunExecutor` registration, so the last one configured wins.

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
// 32 or more random bytes, shared with the runner (see Authorization Posture below)
var runnerKey = Convert.FromBase64String(configuration["Trax:RunnerSigningKey"]!);

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
                remote.SigningKey = runnerKey;
            },
            routing => routing.ForTrain<IMyTrain>())
        // Optional: also offload run* mutations to the remote endpoint
        .UseRemoteRun(remote =>
        {
            remote.BaseUrl = "https://my-workers.example.com/trax/run";
            remote.SigningKey = runnerKey;
        })
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
builder.Services.AddTraxJobRunner(runner =>
    runner.SigningKey = Convert.FromBase64String(builder.Configuration["Trax:RunnerSigningKey"]!)
);

var app = builder.Build();
app.UseTraxJobRunner("/trax/execute");  // queue path
app.UseTraxRunEndpoint("/trax/run");    // synchronous run path
app.Run();
```

The `UseBroadcaster()` call is essential for cross-process subscriptions, without it, the API process has no way to receive lifecycle events from the remote worker. See [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) for details.

**Remote side (AWS Lambda with `Trax.Runner.Lambda`):**

For Lambda deployments, consider using [Lambda Workers (Direct Invocation)](#model-2c-lambda-workers-direct-invocation) instead, it eliminates public endpoints entirely. The HTTP model shown here requires a Function URL or API Gateway, which creates a publicly-routable endpoint.

If you do use HTTP with Lambda, the `TraxLambdaFunction` base class handles service provider lifecycle, envelope-based dispatching, cancellation from Lambda's remaining time, and error handling. `RunLocalAsync()` exposes HTTP endpoints for local development. See the [TraxLambdaFunction API reference](/docs/sdk-reference/scheduler-api/trax-lambda-function) for details.

```
┌──── Scheduler Process ────┐         ┌──── Remote Process ────────────┐
│                            │         │                                │
│  ManifestManager           │         │  POST /trax/execute            │
│  JobDispatcher             │         │       │                        │
│       │                    │         │       ▼                        │
│       ▼                    │  HTTP   │  JobRunnerTrain                │
│  HTTP submitter ───────────┼────────→│  └─→ Your Train               │
│                            │  POST   │                                │
└────────────────────────────┘         └────────────────────────────────┘
                                                │
                                                ▼
                                       Shared PostgreSQL
```

**When to use:**
- **Serverless compute** (AWS Lambda, Google Cloud Run, Azure Functions), trains only run when invoked, zero idle cost
- **Isolation**: trains run in a separate security boundary or VPC
- **Heterogeneous compute**: different train types need different hardware (GPU, high memory)
- **Scaling**: the remote endpoint can auto-scale independently of the scheduler

**Sample:** See `Trax.Samples.ContentShield.Api` and `Trax.Samples.ContentShield.Runner` in the `samples/EphemeralWorkers/` directory of the Trax.Samples repository. The API serves GraphQL and dispatches queued mutations to the Runner via HTTP. No `background_job` table, no DB polling. The Runner uses `UseBroadcaster` with RabbitMQ so GraphQL subscriptions on the API are notified when queued trains complete.

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
            sqs =>
            {
                sqs.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs";
                sqs.SigningKey = runnerKey;
            },
            routing => routing.ForTrain<IMyTrain>())
        // Optional: keep UseRemoteRun for synchronous mutations
        .UseRemoteRun(remote =>
        {
            remote.BaseUrl = "https://my-runner.example.com/trax/run";
            remote.SigningKey = runnerKey;
        })
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

`BuildServiceProvider` registers `AddTrax(...)` and `AddTraxJobRunner(runner => runner.SigningKey = ...)` with the same key as `sqs.SigningKey`. The handler checks each message's signature attribute; it does not check the message's age or refuse a repeat, because SQS redelivers by design and the job's `Pending` metadata row is what stops a second run.

```
┌──── Scheduler Process ────┐         ┌──── SQS ────┐       ┌── Lambda ──────────────┐
│                            │         │              │       │                        │
│  ManifestManager           │         │  trax-jobs   │       │  SqsJobRunnerHandler   │
│  JobDispatcher             │         │  queue       │       │       │                │
│       │                    │   SQS   │              │  SQS  │       ▼                │
│       ▼                    │  Send   │              │ Event │  JobRunnerTrain        │
│  SQS submitter ────────────┼────────→│              │──────→│  └─→ Your Train        │
│                            │         │              │       │                        │
└────────────────────────────┘         └──────────────┘       └────────────────────────┘
                                                                       │
                                                                       ▼
                                                              Shared PostgreSQL
```

**When to use:**
- **AWS Lambda**: event-driven, auto-scaling, zero idle cost with durable message delivery
- **Guaranteed delivery**: SQS retries failed messages and dead-letters after max retries
- **Backpressure**: SQS buffers burst traffic; Lambda drains at a controlled rate
- **High volume**: thousands of concurrent jobs without overwhelming endpoints

**Sample:** The SQS transport is not yet production-ready. See the [Lambda Workers](#model-2c-lambda-workers-direct-invocation) model for the recommended Lambda deployment pattern.

### Model 2c: Lambda Workers (Direct Invocation)

Like Remote Workers but without any public endpoint. The scheduler invokes the Lambda function directly via the AWS SDK. No API Gateway, Function URLs, or HTTP endpoints. Access is controlled entirely by IAM policies.

Requires the `Trax.Scheduler.Lambda` package.

**Scheduler side:**

```csharp
using Trax.Scheduler.Lambda.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(assemblies)
    .AddScheduler(scheduler => scheduler
        .UseLambdaWorkers(
            lambda =>
            {
                lambda.FunctionName = "content-shield-runner";
                lambda.SigningKey = runnerKey;
            },
            routing => routing
                .ForTrain<IReviewContentTrain>()
                .ForTrain<ISendViolationNoticeTrain>())
        // Optional: also offload run* mutations to Lambda
        .UseLambdaRun(lambda =>
        {
            lambda.FunctionName = "content-shield-runner";
            lambda.SigningKey = runnerKey;
        })
    )
);
```

**Lambda function:**

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
                .SkipMigrations()
                .UsePostgres(connString)
                .UseBroadcaster(b => b.UseRabbitMq(rabbitMqConnString)))
            .AddMediator(typeof(MyTrain).Assembly));
    }

    protected override void ConfigureRunner(TraxJobRunnerOptions runner, IConfiguration configuration) =>
        runner.SigningKey = Convert.FromBase64String(configuration["Trax:RunnerSigningKey"]!);
}
```

The `TraxLambdaFunction` base class receives a `LambdaEnvelope` payload directly from the SDK. No HTTP routing is needed. The envelope's `Type` field determines whether the request is a fire-and-forget job execution (`Execute`) or a synchronous train run (`Run`). See the [TraxLambdaFunction API reference](/docs/sdk-reference/scheduler-api/trax-lambda-function) for details.

```
┌──── Scheduler Process ────┐         ┌──── AWS Lambda ─────────────────┐
│                            │         │                                 │
│  ManifestManager           │         │  LambdaEnvelope (Execute/Run)   │
│  JobDispatcher             │         │       │                         │
│       │                    │  SDK    │       ▼                         │
│       ▼                    │ Invoke  │  TraxLambdaFunction             │
│  Lambda submitter ─────────┼────────→│  └─→ ITraxRequestHandler        │
│                            │         │       └─→ Your Train            │
└────────────────────────────┘         └─────────────────────────────────┘
                                                │
                                                ▼
                                       Shared PostgreSQL
```

Two invocation modes:

| Mode | `InvocationType` | Behavior |
|------|-------------------|----------|
| **Execute** (queued trains) | `Event` | Fire-and-forget. scheduler gets 202, Lambda runs async |
| **Run** (synchronous trains) | `RequestResponse` | Scheduler blocks until Lambda completes and returns output |

**When to use:**
- **AWS Lambda**: direct SDK invocation, no public endpoints, access governed by IAM
- **Security-sensitive workloads**: no Function URLs or API Gateway; the Lambda is never publicly reachable
- **Simpler infrastructure**: fewer AWS resources to manage (no API Gateway, no Function URL configuration)
- **Lower latency**: direct invocation avoids the API Gateway routing layer

**Local development:** For local dev and testing, `RunLocalAsync()` starts a Kestrel server that exposes the same `/trax/execute` and `/trax/run` HTTP endpoints. Use `UseRemoteWorkers()` + `UseRemoteRun()` on the scheduler side during development, then switch to `UseLambdaWorkers()` + `UseLambdaRun()` for production deployment.

**IAM permissions:** The scheduler process needs `lambda:InvokeFunction` on the target function ARN. The Lambda execution role needs its normal permissions (database access, etc.).

**Payload size limit:** Lambda invocation payloads are limited to 256 KB (synchronous) and 256 KB (async). If your serialized train input exceeds this, store the data externally and pass a reference.

**Sample:** See `Trax.Samples.ContentShield.Api` and `Trax.Samples.ContentShield.Runner` in the `samples/EphemeralWorkers/` directory of the Trax.Samples repository. The sample uses `UseRemoteWorkers()` for local development with commented-out `UseLambdaWorkers()` configuration for production deployment.

### Model 3: Standalone Workers (Poll-Based)

A separate, always-on process polls the `background_job` table and runs trains. No scheduler logic, just execution.

**Scheduler side** (scheduling only, no local execution):

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

> **Tip:** `OverrideSubmitter` with `PostgresJobSubmitter` gives you a scheduler that only writes to the `background_job` table. No `LocalWorkerService` is started. By default (without `OverrideSubmitter`), local workers are started automatically when Postgres is configured, and you can run standalone workers alongside them for horizontal scaling.

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
- **Separate servers**: dedicated worker machines with different specs
- **Horizontal scaling**: run multiple worker processes, each polling the same table (PostgreSQL `SKIP LOCKED` prevents duplicates)
- **Process isolation**: scheduler crash doesn't kill in-flight trains
- **Kubernetes/ECS**: deploy workers as a separate service with independent scaling

**Sample:** See `Trax.Samples.EnergyHub.Hub` and `Trax.Samples.EnergyHub.Worker` in the `samples/DistributedWorkers/` directory of the Trax.Samples repository for a working example. The Hub combines GraphQL API, scheduler, and dashboard in one process while offloading all train execution to the Worker.

## Which Model Should I Use?

| Scenario | Recommended Model |
|----------|-------------------|
| Single-server deployment | **Local Workers**: simplest setup, no network overhead |
| Separate worker servers (always running) | **Standalone Workers**: poll-based, no HTTP layer needed |
| AWS Lambda (recommended) | **Lambda Workers**: direct SDK invocation, no public endpoints, IAM-governed |
| AWS Lambda with durable queuing | **SQS Workers**: guaranteed delivery, retries, DLQ, auto-scaling |
| Google Cloud Run / Azure Functions | **Remote Workers**: push-based HTTP, matches serverless event model |
| Different hardware per train type | **Remote Workers**: route to GPU/high-memory endpoints |
| Security-sensitive Lambda workloads | **Lambda Workers**: no Function URL or API Gateway needed |
| Just getting started | **Local Workers**: scale out later when you need to |

You can also mix models. For example, run local workers for fast trains and remote workers for expensive GPU trains, using per-train routing with `ForTrain<T>()`:

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

## Authorization Posture

A runner runs what it is sent as trusted infrastructure: the scheduler already authorized the work, so the runner skips per-train `[TraxAuthorize]` checks. Every runner entry point (`UseTraxJobRunner`, `UseTraxRunEndpoint`, `SqsJobRunnerHandler`, `TraxLambdaFunction`) therefore refuses to start until `AddTraxJobRunner(runner => ...)` says who may send it work. The startup error names the choices.

| Posture | Applies to | What the runner does |
|---------|-----------|----------------------|
| `runner.SigningKey = key` | every entry point | Verifies a `Trax-Signature` over the exact request body before reading it. The recommended posture. |
| `runner.AuthorizationPolicy = "name"` | `UseTraxJobRunner`, `UseTraxRunEndpoint` | Applies the named ASP.NET policy. The policy must admit only the scheduler: a policy that admits end users lets them run any registered train, gated or not. |
| `runner.AllowUnsignedRequests()` | every entry point | Accepts every request, and logs a warning naming the entry point at startup. For a runner only the scheduler can reach, such as a Lambda function behind IAM. |

**Signed requests.** Generate 32 or more random bytes (`openssl rand -base64 32`), store them as a secret, and give the same key to both sides: `SigningKey` on `UseRemoteWorkers`, `UseRemoteRun`, `UseLambdaWorkers`, `UseLambdaRun` or `UseSqsWorkers`, and on `AddTraxJobRunner` (or `ConfigureRunner` in a `TraxLambdaFunction`).

```csharp
// Scheduler
.UseRemoteWorkers(remote =>
{
    remote.BaseUrl = "https://my-workers.example.com/trax/execute";
    remote.SigningKey = runnerKey;
})

// Runner
builder.Services.AddTraxJobRunner(runner => runner.SigningKey = runnerKey);
app.UseTraxJobRunner("/trax/execute");
app.UseTraxRunEndpoint("/trax/run");
```

The signature is an HMAC-SHA256 over the request's purpose (`execute` or `run`), a timestamp, a random nonce and the body, carried in the `Trax-Signature` HTTP header, the `LambdaEnvelope`'s `Signature`, or an SQS message attribute of the same name. A request signed for one endpoint does not verify on the other.

| Transport | Checked |
|-----------|---------|
| HTTP (`/trax/execute`, `/trax/run`, and `RunLocalAsync`) | signature, timestamp within `MaxClockSkew` (default 5 minutes), nonce not seen before |
| Lambda `Run` (synchronous) | signature, timestamp, nonce |
| Lambda `Execute` (asynchronous) and SQS | signature only: both redeliver the same message, and the job's `Pending` metadata row stops a second run |

A refused HTTP request gets `401` and never reaches the train. A refused Lambda invocation or SQS message throws, so Lambda's retry and dead-letter settings apply. The scheduler signs each retry afresh, so a retry is never refused as a replay.

**Where nonces are kept.** A signing runner records each accepted nonce in the Trax database, in the `runner_nonce` table the standard migrations create, so every instance of a runner that shares the database accepts a request once between them. The table's primary key decides: a nonce already recorded, and not yet expired, is a repeat. Any other database failure while recording one is an error, never read as a repeat. Expired rows are taken over or removed as it goes. A runner that runs as a single instance can keep them in memory instead with `runner.UseInMemoryNonceStore()`, and a host can register its own `INonceStore` singleton to share them some other way. A signing runner whose host has no relational data provider (`UsePostgres` or `UseSqlite`) must pick one of those two, or it refuses to start.

**Policy-based authorization.** Name the policy in the runner options rather than chaining `.RequireAuthorization()` onto the endpoint, so the runner can check it at startup. The scheduler side adds the credentials the policy expects through `ConfigureHttpClient`:

```csharp
// Scheduler
.UseRemoteWorkers(remote =>
{
    remote.BaseUrl = "https://my-workers.example.com/trax/execute";
    remote.ConfigureHttpClient = client =>
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {schedulerToken}");
})

// Runner
builder.Services.AddAuthorization(o =>
    o.AddPolicy("trax-scheduler", p => p.RequireClaim("scope", "trax:dispatch")));
builder.Services.AddTraxJobRunner(runner => runner.AuthorizationPolicy = "trax-scheduler");

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxJobRunner("/trax/execute");
```

A signing key and a policy can be combined; the runner then requires both.

**Only registered trains run.** A queued job names its input type, and the runner accepts only a name that is the input type of one of its registered trains. The job's metadata row must belong to the train registered for that input; otherwise the runner refuses it and the row stays `Pending`. Registered does not include the scheduler's own trains (the ManifestManager, the JobDispatcher, the JobRunner and the two cleanup trains): the scheduler starts those itself, so `/trax/run` and a Lambda `Run` refuse a name that is one of them, and a queued job whose input belongs to one is refused with its row left `Pending`. A host train that shares one of their short names still runs by that name.

## Host Tracking

Every metadata record automatically captures which host executed the train, hostname, environment type (Lambda, ECS, Kubernetes, etc.), and instance ID. This works across all deployment models with zero configuration. You can also add custom labels (region, service, team) via the builder API.

See [Host Tracking](/docs/effect/host-tracking) for details on auto-detection, custom labels, and querying by host.

## Shared Requirements

Regardless of deployment model, every process that executes trains must:

1. **Reference the same train assemblies**: the train types are resolved by fully-qualified name
2. **Connect to the same Postgres database**: metadata, manifests, and state are shared
3. **Register the effect system**: `AddTrax()` with `UsePostgres()` and `AddMediator()`

## Failure Handling

When the JobDispatcher dispatches a job, the Metadata record is committed to the database **before** the job is submitted to the worker. This is necessary because the worker needs to read the Metadata. However, if the submission fails (network timeout, remote worker unreachable, throttling), the Metadata would be orphaned in `Pending` state.

Trax handles this with multiple layers of protection:

### 1. Retry with Exponential Backoff

All remote submitters (HTTP and Lambda) retry on transient failures with exponential backoff and jitter.

The HTTP submitter and HTTP run executor retry on HTTP 429, 502, and 503. If the server sends a `Retry-After` header, the helper uses that instead of the computed backoff delay.

The Lambda submitter and Lambda run executor retry on AWS status codes 429 (Throttling), 502, 503, and 504, plus network-level `HttpRequestException`.

| Option | Default | Description |
|--------|---------|-------------|
| `Retry.MaxRetries` | 5 | Maximum retry attempts before giving up |
| `Retry.BaseDelay` | 1 second | Starting delay, doubled on each attempt |
| `Retry.MaxDelay` | 30 seconds | Cap on exponential growth |

Configure via the options object on each transport:

```csharp
// HTTP
.UseRemoteWorkers(
    remote =>
    {
        remote.BaseUrl = "https://my-workers.example.com/trax/execute";
        remote.Retry.MaxRetries = 10;
        remote.Retry.BaseDelay = TimeSpan.FromSeconds(2);
    },
    routing => routing.ForTrain<IMyTrain>())

// Lambda
.UseLambdaWorkers(
    lambda =>
    {
        lambda.FunctionName = "my-runner";
        lambda.Retry.MaxRetries = 10;
        lambda.Retry.BaseDelay = TimeSpan.FromSeconds(2);
    },
    routing => routing.ForTrain<IMyOtherTrain>())
```

Set `MaxRetries = 0` to disable retries entirely.

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

### 4. Stale InProgress Reaper

The ManifestManager also runs a `ReapStaleInProgressMetadataJunction` on every polling cycle. Any Metadata that has been in `InProgress` state longer than `StaleInProgressTimeout` (default: 60 minutes) is automatically marked as `Failed`. This catches hard crashes where the worker dies without reaching `FinishServiceTrain`: Lambda hard-kills, OOM events, or process crashes that bypass all .NET exception handling.

```csharp
.AddScheduler(scheduler => scheduler
    .StaleInProgressTimeout(TimeSpan.FromMinutes(45))
    // ...
)
```

This timeout should be longer than `DefaultJobTimeout` (default: 20 minutes) to give cooperative cancellation time to propagate before force-failing. The ordering in the ManifestManager pipeline is: `CancelTimedOutJobsJunction` (cooperative cancel) → `ReapStalePendingMetadataJunction` → `ReapStaleInProgressMetadataJunction` (force-fail) → `ResolveStaleStagedEntriesJunction` → `ReapFailedJobsJunction` (dead-letter).

### 5. Dead-Lettering

After `MaxRetries` failed **executions** (distinct from dispatch attempts), the ManifestManager creates a `DeadLetter` record and marks the manifest as `AwaitingIntervention`. Dead letters can be resolved via the Dashboard or programmatically.

Failed metadata feeds into the normal retry pipeline, if the manifest has retries remaining, the ManifestManager will create a new work queue entry on the next cycle.

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
| `ErrorMessage` | The message of a `TrainException`. Any other exception is reported with a fixed message; the detail stays in the runner's log |
| `ExceptionType` | The .NET exception type name (e.g., `"InvalidOperationException"`) |
| `FailureJunction` | The train junction where the failure occurred (extracted from `TrainExceptionData`) |
| `StackTrace` | Always null. No stack trace leaves the runner; the runner's metadata row and log hold it |
| `FailureClass` | `/trax/run` only (`RemoteRunResponse`). The [failure class](/docs/core/trains-and-junctions#classifying-failures) the worker's classifier assigned, or null when the worker sent none |
| `PublicMessage` | `/trax/run` only (`RemoteRunResponse`). The message a client of the calling side may see: the message of a plain `TrainException`, which a train author wrote for the caller, and null for every other failure |

On the API side, the HTTP job submitter and HTTP run executor read the response body and reconstruct a `TrainException` with the structured data intact. The HTTP submitter counts a job as submitted only when the body is a `RemoteJobResponse` for the job it sent; any other `200` fails the dispatch. On the run path it is a `RemoteRunException`, which carries the runner's `PublicMessage`; a transport failure (a non-success status, a Lambda function error, an empty reply) is a `RemoteRunException` with no public message. A surface that shows errors to clients reads `PublicMessage` and, when it is null, says only that the train failed. `Trax.Docs/adr/0028` records why. `Metadata.AddException()` populates `FailureException`, `FailureJunction`, `FailureReason` and (for `/trax/run`) `FailureClass` from the reconstructed exception. The class is carried rather than recomputed, because the original exception type is gone by the time the response arrives; a null `FailureClass` records as `Unclassified`, and the calling side's own classifier is never asked about a failure rebuilt from the response. The job-runner HTTP endpoint and the Lambda runner's local HTTP route write `RemoteRunResponse` with Trax's own JSON options (enums as integers) whatever the host's JSON configuration. A Lambda function's own invocation response is serialized by the function's Lambda serializer, which Trax does not control. Both the HTTP and Lambda run executors therefore read `FailureClass` as either an integer or a name, and a class they do not know (an unknown number or name from a newer worker) reads as `Unclassified` while the worker's error is kept. `/trax/execute` needs no such field: the worker writes to the same metadata row, so its classification is already recorded. Locally-executed trains attach this data via `Exception.Data["TrainExceptionData"]`; remote trains carry it as JSON in the exception message instead. On the worker, the error fields come from the attached data, or, for a `TrainException` rebuilt from an earlier boundary, from the JSON in its message. Any other exception is sent with a fixed message and no `FailureClass`; its detail stays in the runner's log. A class outside the `FailureClass` values is sent as `Unclassified`.

```
Runner Process                         API Process
─────────────────                      ───────────────────
Train fails with exception
    │
    ▼
TraxRequestHandler catches exception
Extracts: Type, Junction, Message
    │
    ▼
RemoteRunResponse / RemoteJobResponse
(structured error fields)
    │
    ├───── HTTP 200 + JSON body ──────→ HTTP run executor / job submitter
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

If the HTTP call itself fails (network error, infrastructure 5xx before reaching the endpoint), the error body is read and included in the exception message for debugging, you'll see the HTTP status code and the response body rather than a generic "500 Internal Server Error".

That detail is for the metadata row and the logs, not for GraphQL clients. When a remote run's failure reaches the GraphQL error filter, only a `TrainException`'s own message passes through: a worker that threw `TrainException("Order 42 is already closed")` shows the client that sentence, while a worker that failed with any other exception type, or an endpoint that answered with a non-success status, shows `"The train failed."` with code `TRAX_TRAIN_ERROR`.

### Debugging Remote Failures

When a remote job fails, check these in order:

1. **Metadata table**: `SELECT failure_exception, failure_junction, failure_reason, stack_trace FROM trax.metadata WHERE id = <id>`. These fields are populated from the structured error response.
2. **Log table**: `SELECT * FROM trax.log WHERE metadata_id = <id> ORDER BY id`. If `AddDataContextLogging()` is enabled on the runner, junction-level logs are persisted.
3. **Stale pending check**: If `failure_exception = 'StalePendingTimeout'`, the runner never started executing. Check runner health, network connectivity, and deployment status.

## Limitations

- **Cancellation is process-local.** The `ICancellationRegistry` is in-memory. Dashboard "Cancel" only cancels trains running on the same process as the dashboard. Remote trains cannot be cancelled via the dashboard in v1.
- **Type resolution requires shared assemblies.** The remote process must reference the same NuGet packages and assemblies that define your train types, and register them with `AddMediator`. A queued job's input type is matched by fully-qualified name against the registered trains' input types, and a remote run's output is read into the output type the caller expects. When that type is an interface or abstract, the output is read into the implementation the runner names, but only if that implementation is already loaded in the scheduler's process and implements the expected type; the scheduler never loads a type by the name the runner sends, and refuses the run otherwise. So the scheduler must reference the assembly that defines the concrete output too.

## See Also

- [Job Submission](/docs/scheduler/job-submission): architecture of the job submission pipeline
- [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers): API reference for local worker configuration
- [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers): API reference for remote workers (HTTP)
- [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers): API reference for Lambda workers (direct SDK invocation)
- [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run): API reference for Lambda run execution (direct SDK invocation)
- [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers): API reference for SQS workers (Lambda)
- [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run): API reference for remote run execution (HTTP)
- [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner): API reference for remote receiver setup
- [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker): API reference for standalone worker setup

## SDK Reference

> [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) | [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run) | [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers) | [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run) | [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers) | [AddTraxJobRunner / UseTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner) | [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers) | [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function) | [OverrideSubmitter](/docs/sdk-reference/scheduler-api/add-scheduler) | [StalePendingTimeout / StaleInProgressTimeout](/docs/sdk-reference/scheduler-api/add-scheduler)
