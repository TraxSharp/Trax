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
| `routing` | `Action<SubmitterRouting>?` | No | Callback to specify which trains should be dispatched to this remote endpoint. When omitted, no train is routed here explicitly; `[TraxRemote]`-attributed trains are, if this is the first `UseRemoteWorkers`, `UseSqsWorkers` or `UseLambdaWorkers` call. |

## Returns

`SchedulerConfigurationBuilder`, for continued fluent chaining.

## RemoteWorkerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BaseUrl` | `string` | _(required)_ | The URL of the remote endpoint that receives job requests (e.g., `https://my-workers.example.com/trax/execute`) |
| `ConfigureHttpClient` | `Action<HttpClient>?` | `null` | Optional callback to configure the `HttpClient` (add auth headers, custom timeouts, or any other HTTP configuration) |
| `Timeout` | `TimeSpan` | 30 seconds | HTTP request timeout for each job dispatch. A job the runner has already started when it expires keeps running there and is not dispatched again |
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
        .Schedule<IMyTrain>("my-job", new MyInput(), Every.Minutes(5))
        .Schedule<IHeavyComputeTrain>("heavy", new HeavyInput(), Every.Hours(1))
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

Call `UseRemoteWorkers()` once per endpoint to route different trains to different runners:

```csharp
.AddScheduler(scheduler => scheduler
    .UseRemoteWorkers(
        remote =>
        {
            remote.BaseUrl = "https://gpu-workers/trax/execute";
            remote.SigningKey = gpuRunnerKey;
        },
        routing => routing.ForTrain<IAiInferenceTrain>())
    .UseRemoteWorkers(
        remote =>
        {
            remote.BaseUrl = "https://cpu-workers/trax/execute";
            remote.SigningKey = cpuRunnerKey;
        },
        routing => routing.ForTrain<IBatchProcessTrain>())
)
```

Each call keeps its own `RemoteWorkerOptions` and its own `HttpClient`. `IAiInferenceTrain` jobs are sent only to `gpu-workers`, signed with `gpuRunnerKey`, and carry only the headers that call's `ConfigureHttpClient` added; `IBatchProcessTrain` jobs go only to `cpu-workers` with its key and headers. Neither runner ever sees the other's credentials.

Each train can only be routed to one submitter. Routing the same train from two calls (or from `UseRemoteWorkers()` and `UseSqsWorkers()` or `UseLambdaWorkers()`) throws `InvalidOperationException` when the scheduler is built, naming both endpoints.

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

A train marked with `[TraxRemote]` that no call routes with `ForTrain<T>()` is dispatched to the **first** routed registration, of whatever kind: the first `UseRemoteWorkers()`, `UseSqsWorkers()` or `UseLambdaWorkers()` call in the builder. With `UseSqsWorkers()` alone, `[TraxRemote]` trains go to that queue; with two `UseRemoteWorkers()` calls, they go to the first endpoint. Builder `ForTrain<T>()` routing takes precedence over the attribute.

A scheduler with `[TraxRemote]` trains and none of the three fails when it is built, naming each such train: running a train marked remote on the scheduler host, often the one host it was kept off for isolation, is not a fallback. Add one of the three calls, or remove the attribute from a train that should run locally.

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

Each `UseRemoteWorkers()` call registers:

| Service | Lifetime | Description |
|---------|----------|-------------|
| A named `HttpClient` | Per `IHttpClientFactory` | The call's own client, with its `BaseUrl`, `Timeout` and `ConfigureHttpClient` applied |
| HTTP job submitter | Created per dispatch | An internal `IJobSubmitter` that dispatches jobs via HTTP POST with the call's own options and client. The JobDispatcher creates it for each train routed to this call; application code does not resolve it |

`RemoteWorkerOptions` is not registered in the container: each call's options belong to its own submitter.

> **Note:** `UseRemoteWorkers()` does **not** replace the default `IJobSubmitter`. Local workers continue to run for trains not routed to this endpoint.

## How It Works

When the JobDispatcher processes a work queue entry, it checks whether the entry's train is routed to these remote workers. If it is, the HTTP submitter:

1. Serializes a `RemoteJobRequest` containing the metadata ID and optional input
2. POSTs the JSON payload to `BaseUrl`
3. Reads the runner's `RemoteJobResponse` for that metadata ID. A non-success status, an `IsError` response, or a success status whose body is not a `RemoteJobResponse` naming the same metadata ID (a proxy's page, an empty body, a misrouted `BaseUrl`) fails the submit with a `TrainException`
4. Returns a synthetic job ID (`"http-{guid}"`)

The remote endpoint is responsible for running `JobRunnerTrain`, which loads the metadata from the shared Postgres database, claims the run, executes the train, and updates the manifest.

A failed submit is not always a failed delivery. When the submit fails, the dispatcher looks at the run's row: if the runner has already started it (the runner ran the train and reported its failure, or is still running it when `Timeout` expires), the run is the runner's and its outcome is recorded there, so the entry is **not** requeued and the train does not run again. Only a run still `Pending` (the request never reached a runner, or the runner refused it before starting it) is recorded as a failed dispatch attempt and requeued, up to [`MaxDispatchAttempts`](/docs/scheduler/admin-trains/job-dispatcher#dispatch-failures). See [Delivery and execution](/docs/scheduler/remote-execution#delivery-and-execution).

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
