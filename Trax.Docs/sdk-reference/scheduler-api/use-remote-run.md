---
title: UseRemoteRun
description: Reference for UseRemoteRun, which sends synchronous run requests to a remote HTTP runner, with RemoteRunOptions and what it registers.
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 11
package: Trax.Scheduler
namespace: Trax.Scheduler.Configuration
source:
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/SchedulerConfigurationBuilder/SchedulerConfigurationBuilder.Settings.cs
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/RemoteRunOptions.cs
---

# UseRemoteRun

Sends synchronous runs to a remote HTTP endpoint, a runner that maps
[`UseTraxRunEndpoint`](/docs/sdk-reference/scheduler-api/add-trax-job-runner), instead of running
them in process. A synchronous run is every run that answers its caller with the output: a GraphQL
mutation in `RUN` mode, a `[TraxQuery]` query, and `ITrainExecutionService.RunAsync`. The call
blocks until the remote train completes and returns its output.

## Signature

```csharp fragment
public SchedulerConfigurationBuilder UseRemoteRun(
    Action<RemoteRunOptions> configure
)
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `configure` | `Action<RemoteRunOptions>` | Sets the endpoint URL and HTTP client options |

### RemoteRunOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BaseUrl` | `string` | required | The URL that receives run requests, for example `https://my-runner.example.com/trax/run` |
| `ConfigureHttpClient` | `Action<HttpClient>?` | `null` | Configures the `HttpClient`: headers, and anything else the runner expects |
| `Timeout` | `TimeSpan` | 5 minutes | The request timeout. A run request waits for the train to complete |
| `Retry` | [`HttpRetryOptions`](/docs/sdk-reference/scheduler-api/use-remote-workers#httpretryoptions) | 5 retries, 1 second base, 30 seconds cap | Retries on HTTP 429, 502 and 503 |
| `SigningKey` | `byte[]?` | `null` | The key shared with the runner's `AddTraxJobRunner(runner => runner.SigningKey = ...)`, at least 32 bytes. When set, each request, and each retry, carries a `Trax-Signature` the runner verifies. See [Authorization Posture](/docs/scheduler/remote-execution#authorization-posture) |

## Returns

`SchedulerConfigurationBuilder`, for chaining.

## Behavior

- Every synchronous run, for every train, goes to the runner, including `[TraxQuery]` queries.
  The caller is authorized and the input read on the calling host first.
- A run is POSTed as a `RemoteRunRequest` (the train name, input JSON and input type). A
  `RemoteRunResponse` with the output returns it; an error throws `RemoteRunException`, a
  `TrainException` whose `PublicMessage` is the runner's message for a client, or null for a
  transport failure or any failure other than a train author's `TrainException`.
- A signed run request must reach the runner within its `MaxClockSkew` (five minutes by default)
  and is accepted once.
- The runner calls `ITrainExecutionService.RunAsync` itself, so the run's metadata is in the
  shared database.

### Registered services

| Service | Lifetime | Description |
|---------|----------|-------------|
| `RemoteRunOptions` | Singleton | The options |
| `IRunExecutor` | Transient | A typed `HttpClient` client that POSTs each run and waits for the response. It replaces the default `LocalRunExecutor`, which runs trains in process through `ITrainBus.RunByNameAsync` |

## Example

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(assembly)
    .AddScheduler(scheduler => scheduler
        .UseRemoteWorkers(
            remote => remote.BaseUrl = "https://my-runner.example.com/trax/execute",
            routing => routing.ForTrain<IReviewContentTrain>())
        .UseRemoteRun(remote =>
        {
            remote.BaseUrl = "https://my-runner.example.com/trax/run";
            remote.SigningKey = Convert.FromBase64String(configuration["Trax:RunnerSigningKey"]!);
            remote.Timeout = TimeSpan.FromMinutes(10);
        })));
```

## See also

- [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner), the runner side, with `UseTraxRunEndpoint`
- [How remote dispatch works](/docs/scheduler/how-remote-dispatch-works), run executors against submitters
- [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers), queued runs over HTTP
- [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run), synchronous runs on Lambda
