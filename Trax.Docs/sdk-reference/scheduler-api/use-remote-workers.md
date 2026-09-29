---
layout: default
title: UseRemoteWorkers
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 9
---

# UseRemoteWorkers

Routes specific trains to a remote HTTP endpoint for execution. Trains not included in the routing configuration continue to execute locally via `PostgresJobSubmitter` and `LocalWorkerService`.

## Signature

```csharp
public SchedulerConfigurationBuilder UseRemoteWorkers(
    Action<RemoteWorkerOptions> configure,
    Action<SubmitterRouting>? routing = null
)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configure` | `Action<RemoteWorkerOptions>` | Yes | Callback to set the remote endpoint URL and HTTP client options |
| `routing` | `Action<SubmitterRouting>?` | No | Callback to specify which trains should be dispatched to this remote endpoint. When omitted, only `[TraxRemote]`-attributed trains are routed. |

## Returns

`SchedulerConfigurationBuilder`, for continued fluent chaining.

## RemoteWorkerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BaseUrl` | `string` | _(required)_ | The URL of the remote endpoint that receives job requests (e.g., `https://my-workers.example.com/trax/execute`) |
| `ConfigureHttpClient` | `Action<HttpClient>?` | `null` | Optional callback to configure the `HttpClient` (add auth headers, custom timeouts, or any other HTTP configuration) |
| `Timeout` | `TimeSpan` | 30 seconds | HTTP request timeout for each job dispatch |
| `Retry` | `HttpRetryOptions` | _(see below)_ | Retry options for transient HTTP failures (429, 502, 503) |
| `SigningKey` | `byte[]?` | `null` | The key shared with the runner's `AddTraxJobRunner(runner => runner.SigningKey = ...)`, at least 32 bytes. When set, each request (and each retry) carries a `Trax-Signature` the runner verifies. See [Authorization Posture](/docs/scheduler/remote-execution#authorization-posture) |

### HttpRetryOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRetries` | `int` | 5 | Maximum retry attempts. Set to 0 to disable retries. |
| `BaseDelay` | `TimeSpan` | 1 second | Starting delay between retries (doubled on each attempt with ±25% jitter) |
| `MaxDelay` | `TimeSpan` | 30 seconds | Maximum delay cap to prevent unbounded exponential growth |

Retries on HTTP 429 (Too Many Requests), 502 (Bad Gateway), and 503 (Service Unavailable). Respects the `Retry-After` header when present.

## SubmitterRouting

| Method | Description |
|--------|-------------|
| `ForTrain<TTrain>()` | Routes the specified train type to this remote endpoint. Returns the routing instance for chaining. |

## Examples

### Basic Usage

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(assemblies)
    .AddScheduler(scheduler => scheduler
        .UseRemoteWorkers(
            remote => remote.BaseUrl = "https://my-workers.example.com/trax/execute",
            routing => routing
                .ForTrain<IHeavyComputeTrain>()
                .ForTrain<IAiInferenceTrain>())
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
        .Schedule<IHeavyComputeTrain, HeavyInput>("heavy", new HeavyInput(), Every.Hours(1))
    )
);
```

In this example, `IHeavyComputeTrain` and `IAiInferenceTrain` are dispatched to the remote endpoint. `IMyTrain` executes locally via the default `PostgresJobSubmitter`.

### With a Signing Key

The runner refuses requests that do not meet its posture. Share a signing key with it:

```csharp
.UseRemoteWorkers(
    remote =>
    {
        remote.BaseUrl = "https://my-workers.example.com/trax/execute";
        remote.SigningKey = Convert.FromBase64String(configuration["Trax:RunnerSigningKey"]!);
    },
    routing => routing.ForTrain<IHeavyComputeTrain>())
```

For a runner that uses an authorization policy instead, add the credentials it expects with `ConfigureHttpClient`:

```csharp
remote.ConfigureHttpClient = client =>
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {schedulerToken}");
```

### With Custom Timeout

```csharp
.UseRemoteWorkers(
    remote =>
    {
        remote.BaseUrl = "https://my-workers.example.com/trax/execute";
        remote.Timeout = TimeSpan.FromMinutes(2);
    },
    routing => routing.ForTrain<IHeavyComputeTrain>())
```

### Multiple Remote Endpoints

You can call `UseRemoteWorkers()` multiple times to route different trains to different endpoints:

```csharp
.AddScheduler(scheduler => scheduler
    .UseRemoteWorkers(
        remote => remote.BaseUrl = "https://gpu-workers/trax/execute",
        routing => routing.ForTrain<IAiInferenceTrain>())
    .UseRemoteWorkers(
        remote => remote.BaseUrl = "https://cpu-workers/trax/execute",
        routing => routing.ForTrain<IBatchProcessTrain>())
)
```

Each train can only be routed to one submitter. Routing the same train to multiple endpoints throws `InvalidOperationException` at build time.

### Attribute-Based Routing

Trains can opt into remote execution via the `[TraxRemote]` attribute instead of explicit `ForTrain<T>()` calls:

```csharp
using Trax.Effect.Attributes;

[TraxRemote]
public class HeavyComputeTrain : ServiceTrain<HeavyInput, HeavyOutput>, IHeavyComputeTrain
{
    // ...
}
```

When `UseRemoteWorkers()` is configured, trains marked with `[TraxRemote]` are automatically dispatched to the first registered remote submitter. Builder `ForTrain<T>()` routing takes precedence over the attribute.

If no `UseRemoteWorkers()` is configured, `[TraxRemote]` is silently ignored and the train runs locally.

## Performance

By default, the JobDispatcher dispatches entries sequentially, one at a time. For local workers (`PostgresJobSubmitter`), this is fine because `EnqueueAsync` just inserts a database row (microseconds). But for the HTTP submitter, each dispatch blocks until the remote endpoint finishes executing the train. If each Lambda invocation takes 2 seconds and 50 entries are eligible, a single dispatch cycle takes ~100 seconds.

Use `MaxConcurrentDispatch` to parallelize HTTP dispatch:

```csharp
.AddScheduler(scheduler => scheduler
    .MaxConcurrentDispatch(10)
    .UseRemoteWorkers(
        remote => remote.BaseUrl = "https://my-workers.example.com/trax/execute",
        routing => routing.ForTrain<IHeavyComputeTrain>())
)
```

This dispatches up to 10 entries concurrently within a single polling cycle, bounded by a `SemaphoreSlim`. The `FOR UPDATE SKIP LOCKED` pattern guarantees safe concurrent dispatch with no duplicate Metadata records, even with intra-cycle parallelism.

Keep `MaxConcurrentDispatch` well below your database connection pool size (default Npgsql pool: 100), since each concurrent dispatch opens its own DI scope and database connection.

See [Parallel Dispatch](/docs/scheduler/admin-trains/job-dispatcher#parallel-dispatch) for details.

## Routing Precedence

1. **Builder `ForTrain<T>()`** (highest priority)
2. **`[TraxRemote]` attribute** (if no builder routing for this train)
3. **Default local `IJobSubmitter`** (fallback for everything else)

## Registered Services

`UseRemoteWorkers()` registers:

| Service | Lifetime | Description |
|---------|----------|-------------|
| `RemoteWorkerOptions` | Singleton | Configuration options |
| HTTP job submitter | Scoped | An internal `IJobSubmitter` that dispatches jobs via HTTP POST. The JobDispatcher resolves it for each train routed to this endpoint; application code does not resolve it |

> **Note:** `UseRemoteWorkers()` does **not** replace the default `IJobSubmitter`. Local workers continue to run for trains not routed to this endpoint.

## How It Works

When the JobDispatcher processes a work queue entry, it checks whether the entry's train is routed to these remote workers. If it is, the HTTP submitter:

1. Serializes a `RemoteJobRequest` containing the metadata ID and optional input
2. POSTs the JSON payload to `BaseUrl`
3. Reads the runner's `RemoteJobResponse` for that metadata ID. A non-success status, an `IsError` response, or a success status whose body is not a `RemoteJobResponse` naming the same metadata ID (a proxy's page, an empty body, a misrouted `BaseUrl`) fails the dispatch with a `TrainException`
4. Returns a synthetic job ID (`"http-{guid}"`)

The remote endpoint is responsible for running `JobRunnerTrain`, which loads the metadata from the shared Postgres database, validates the job state, executes the train, and updates the manifest.

## Package

```
dotnet add package Trax.Scheduler
```

## See Also

- [Remote Execution](/docs/scheduler/remote-execution): architecture overview and deployment models
- [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner): setting up the remote receiver endpoint
- [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers): customizing the local (default) execution backend
- [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers): Lambda-based per-train dispatch (direct SDK invocation)
- [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers): SQS-based per-train dispatch
