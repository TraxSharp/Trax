---
title: UseRemoteWorkers
description: Reference for UseRemoteWorkers, which routes chosen trains' queued runs to a remote HTTP endpoint, with RemoteWorkerOptions, HttpRetryOptions and routing.
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 10
package: Trax.Scheduler
namespace: Trax.Scheduler.Configuration
source:
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/SchedulerConfigurationBuilder/SchedulerConfigurationBuilder.Settings.cs
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/RemoteWorkerOptions.cs
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/HttpRetryOptions.cs
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/SubmitterRouting.cs
---

# UseRemoteWorkers

Routes chosen trains' queued runs to a remote HTTP endpoint, a runner that maps
[`UseTraxJobRunner`](/docs/sdk-reference/scheduler-api/add-trax-job-runner). Trains not routed here
keep running on the local workers.

## Signature

```csharp fragment
public SchedulerConfigurationBuilder UseRemoteWorkers(
    Action<RemoteWorkerOptions> configure,
    Action<SubmitterRouting>? routing = null
)
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `configure` | `Action<RemoteWorkerOptions>` | Sets the endpoint URL and HTTP client options |
| `routing` | `Action<SubmitterRouting>?` | Names the trains sent to this endpoint with `ForTrain<TTrain>()`, which returns the routing for chaining. When omitted, no train is routed here by name; `[TraxRemote]` trains are, if this is the first `UseRemoteWorkers`, `UseSqsWorkers` or `UseLambdaWorkers` call |

### RemoteWorkerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BaseUrl` | `string` | required | The URL that receives job requests, for example `https://my-workers.example.com/trax/execute` |
| `ConfigureHttpClient` | `Action<HttpClient>?` | `null` | Configures the `HttpClient`: headers such as `Authorization`, and anything else the runner expects |
| `Timeout` | `TimeSpan` | 30 seconds | The timeout of each dispatch request. A job the runner has already started when it expires keeps running there and is not dispatched again |
| `Retry` | `HttpRetryOptions` | see below | Retries for transient HTTP failures |
| `SigningKey` | `byte[]?` | `null` | The key shared with the runner's `AddTraxJobRunner(runner => runner.SigningKey = ...)`, at least 32 bytes. When set, each request, and each retry, carries a `Trax-Signature` the runner verifies. See [Authorization Posture](/docs/scheduler/remote-execution#authorization-posture) |

### HttpRetryOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRetries` | `int` | 5 | Retry attempts; 0 disables retrying |
| `BaseDelay` | `TimeSpan` | 1 second | The first delay, doubled on each attempt with plus or minus 25% jitter |
| `MaxDelay` | `TimeSpan` | 30 seconds | The cap on any one delay |

Retried: HTTP 429, 502 and 503. A `Retry-After` header is honoured.

## Returns

`SchedulerConfigurationBuilder`, for chaining.

## Behavior

- A routed entry is POSTed to `BaseUrl` as a `RemoteJobRequest` (the metadata id and the input,
  if any). The submit fails with a `TrainException` on a non-success status, an `IsError`
  response, or a success status whose body is not a `RemoteJobResponse` for the same metadata id.
  The job id is `"http-{guid}"`.
- When a submit fails, the entry is requeued only while the run is still `Pending`; a run the
  runner already started is the runner's. See
  [How remote dispatch works](/docs/scheduler/how-remote-dispatch-works#the-submitters).
- `UseRemoteWorkers()` may be called once per endpoint. Each call keeps its own options and
  `HttpClient`, so one endpoint never receives another's headers or signing key.
- A train can be routed to one submitter only. Routing it from two calls, or from this and
  `UseSqsWorkers()` or `UseLambdaWorkers()`, throws `InvalidOperationException` when the scheduler
  is built, naming both endpoints.
- A train with `[TraxRemote]` that no call names goes to the first `UseRemoteWorkers()`,
  `UseSqsWorkers()` or `UseLambdaWorkers()` call in the builder. `ForTrain<T>()` takes precedence
  over the attribute. A scheduler with `[TraxRemote]` trains and none of the three fails when it is
  built, naming each such train.
- The JobDispatcher waits for each request; `MaxConcurrentDispatch` on
  [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) sends several of a cycle at once.

### Registered services

| Service | Lifetime | Description |
|---------|----------|-------------|
| A named `HttpClient` | Per `IHttpClientFactory` | The call's own client, with its `BaseUrl`, `Timeout` and `ConfigureHttpClient` applied |
| HTTP job submitter | Created per dispatch | An internal `IJobSubmitter` the JobDispatcher creates for each train routed to this call |

`RemoteWorkerOptions` is not registered in the container, and the default `IJobSubmitter` is not
replaced.

## Examples

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(assembly)
    .AddScheduler(scheduler => scheduler
        .MaxConcurrentDispatch(10)
        .UseRemoteWorkers(
            remote =>
            {
                remote.BaseUrl = "https://my-workers.example.com/trax/execute";
                remote.SigningKey = Convert.FromBase64String(configuration["Trax:RunnerSigningKey"]!);
                remote.Timeout = TimeSpan.FromMinutes(2);
            },
            routing => routing
                .ForTrain<IReviewContentTrain>()
                .ForTrain<IBatchProcessTrain>())
        .Schedule<ISyncTrain>("sync", new SyncInput(), Every.Minutes(5))));
```

A runner behind an authorization policy rather than a signing key gets its credentials from
`ConfigureHttpClient`:

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(assembly)
    .AddScheduler(scheduler => scheduler
        .UseRemoteWorkers(
            remote =>
            {
                remote.BaseUrl = "https://gpu-workers/trax/execute";
                remote.ConfigureHttpClient = client =>
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {configuration["Gpu:Token"]}");
            },
            routing => routing.ForTrain<IReviewContentTrain>())));
```

## See also

- [How remote dispatch works](/docs/scheduler/how-remote-dispatch-works)
- [Remote Execution](/docs/scheduler/remote-execution), choosing where trains run
- [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner), the receiving side
- [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run), synchronous runs over HTTP
- [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers) and [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers), the other transports
