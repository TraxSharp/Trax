---
layout: default
title: UseRemoteRun
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 9.1
---

# UseRemoteRun

Configures the scheduler to offload synchronous `run` execution to a remote HTTP endpoint instead of executing in-process. The call blocks until the remote train completes and returns the output.

## Signature

```csharp
public SchedulerConfigurationBuilder UseRemoteRun(
    Action<RemoteRunOptions> configure
)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configure` | `Action<RemoteRunOptions>` | Yes | Callback to set the remote endpoint URL and HTTP client options |

## Returns

`SchedulerConfigurationBuilder` — for continued fluent chaining.

## RemoteRunOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BaseUrl` | `string` | _(required)_ | The URL of the remote endpoint that receives run requests (e.g., `https://my-runner.example.com/trax/run`) |
| `ConfigureHttpClient` | `Action<HttpClient>?` | `null` | Optional callback to configure the `HttpClient` — add auth headers, custom timeouts, or any other HTTP configuration |
| `Timeout` | `TimeSpan` | 5 minutes | HTTP request timeout. Longer default than `UseRemoteWorkers` (30s) because run requests block until the train completes |

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
            remote => remote.BaseUrl = "https://my-runner.example.com/trax/execute",
            routing => routing.ForTrain<IMyTrain>()
        )
        .UseRemoteRun(remote =>
            remote.BaseUrl = "https://my-runner.example.com/trax/run"
        )
    )
);
```

### With Authentication

```csharp
.UseRemoteRun(remote =>
{
    remote.BaseUrl = "https://my-runner.example.com/trax/run";
    remote.ConfigureHttpClient = client =>
        client.DefaultRequestHeaders.Add("Authorization", "Bearer my-token");
})
```

### With Custom Timeout

```csharp
.UseRemoteRun(remote =>
{
    remote.BaseUrl = "https://my-runner.example.com/trax/run";
    remote.Timeout = TimeSpan.FromMinutes(10);
})
```

## Remote Side Setup

The remote process must map the run endpoint with `UseTraxRunEndpoint()`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(MyTrain).Assembly)
);

var app = builder.Build();
app.UseTraxRunEndpoint("/trax/run");
app.Run();
```

If the remote also handles queued jobs, add both endpoints:

```csharp
builder.Services.AddTraxJobRunner();

var app = builder.Build();
app.UseTraxJobRunner("/trax/execute");  // queue path
app.UseTraxRunEndpoint("/trax/run");    // synchronous run path
app.Run();
```

## Registered Services

`UseRemoteRun()` registers:

| Service | Lifetime | Description |
|---------|----------|-------------|
| `RemoteRunOptions` | Singleton | Configuration options |
| `IRunExecutor` -> `HttpRunExecutor` | Scoped | Dispatches run requests via HTTP POST, blocks until response |

> **Note:** Without `UseRemoteRun()`, the default `LocalRunExecutor` executes trains in-process via `ITrainBus.RunAsync()`. `UseRemoteRun()` overrides this via last-registration-wins.

## How It Works

When a GraphQL `run*` mutation is called, the `HttpRunExecutor`:

1. Serializes a `RemoteRunRequest` containing the train name, input JSON, and input type
2. POSTs the JSON payload to `BaseUrl`
3. Blocks until the remote endpoint returns a `RemoteRunResponse`
4. On success: deserializes the train output from the response and returns it to GraphQL
5. On error: throws a `TrainException` with the remote error message

The remote endpoint (`UseTraxRunEndpoint`) calls `ITrainExecutionService.RunAsync()` locally — which creates metadata, runs the train, and returns the output. Since both processes share the same Postgres database, the metadata is visible to the dashboard.

## Differences from UseRemoteWorkers

| | UseRemoteRun | UseRemoteWorkers |
|---|---|---|
| **Execution path** | `run*` mutations | `queue*` mutations |
| **Blocking** | Yes — blocks until train completes | No — returns immediately with WorkQueueId |
| **Returns** | Train output (deserialized from response) | WorkQueueId + ExternalId |
| **Remote endpoint** | `UseTraxRunEndpoint()` (`/trax/run`) | `UseTraxJobRunner()` (`/trax/execute`) |
| **Abstraction** | `IRunExecutor` | `IJobSubmitter` |
| **Default timeout** | 5 minutes | 30 seconds |

## Package

```
dotnet add package Trax.Scheduler
```

## See Also

- [Remote Execution]({{ site.baseurl }}{% link scheduler/remote-execution.md %}) — architecture overview and deployment models
- [UseRemoteWorkers]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-remote-workers.md %}) — remote dispatch for queued trains
- [AddTraxJobRunner]({{ site.baseurl }}{% link sdk-reference/scheduler-api/add-trax-job-runner.md %}) — remote receiver setup (includes `UseTraxRunEndpoint`)
