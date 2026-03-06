---
layout: default
title: UseRemoteWorkers
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 9
---

# UseRemoteWorkers

Configures the scheduler to dispatch jobs via HTTP POST to a remote endpoint instead of executing them locally.

## Signature

```csharp
public SchedulerConfigurationBuilder UseRemoteWorkers(
    Action<RemoteWorkerOptions> configure
)
```

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configure` | `Action<RemoteWorkerOptions>` | Yes | Callback to set the remote endpoint URL and HTTP client options |

## Returns

`SchedulerConfigurationBuilder` — for continued fluent chaining.

## RemoteWorkerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BaseUrl` | `string` | _(required)_ | The URL of the remote endpoint that receives job requests (e.g., `https://my-workers.example.com/trax/execute`) |
| `ConfigureHttpClient` | `Action<HttpClient>?` | `null` | Optional callback to configure the `HttpClient` — add auth headers, custom timeouts, or any other HTTP configuration |
| `Timeout` | `TimeSpan` | 30 seconds | HTTP request timeout for each job dispatch |

## Examples

### Basic Usage

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
        })
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
    )
);
```

### With Authentication

Trax doesn't bake in any auth — use `ConfigureHttpClient` to add whatever headers your endpoint expects:

```csharp
.UseRemoteWorkers(remote =>
{
    remote.BaseUrl = "https://my-workers.example.com/trax/execute";
    remote.ConfigureHttpClient = client =>
        client.DefaultRequestHeaders.Add("Authorization", "Bearer my-token");
})
```

### With Custom Timeout

```csharp
.UseRemoteWorkers(remote =>
{
    remote.BaseUrl = "https://my-workers.example.com/trax/execute";
    remote.Timeout = TimeSpan.FromMinutes(2);
})
```

## Registered Services

`UseRemoteWorkers()` registers:

| Service | Lifetime | Description |
|---------|----------|-------------|
| `RemoteWorkerOptions` | Singleton | Configuration options |
| `IJobSubmitter` → `HttpJobSubmitter` | Scoped | Dispatches jobs via HTTP POST |

> **Note:** `UseRemoteWorkers()` does **not** register `LocalWorkerService`. Execution happens entirely on the remote endpoint. If you need the scheduler to also run workers locally, use `UseLocalWorkers()` instead.

## How It Works

When the JobDispatcher calls `IJobSubmitter.EnqueueAsync()`, the `HttpJobSubmitter`:

1. Serializes a `RemoteJobRequest` containing the metadata ID and optional input
2. POSTs the JSON payload to `BaseUrl`
3. Returns a synthetic job ID (`"http-{guid}"`)

The remote endpoint is responsible for running `JobRunnerTrain` — which loads the metadata from the shared Postgres database, validates the job state, executes the train, and updates the manifest.

## Package

```
dotnet add package Trax.Scheduler
```

## See Also

- [Remote Execution]({{ site.baseurl }}{% link scheduler/remote-execution.md %}) — architecture overview and deployment models
- [AddTraxJobRunner]({{ site.baseurl }}{% link sdk-reference/scheduler-api/add-trax-job-runner.md %}) — setting up the remote receiver endpoint
- [UseLocalWorkers]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-local-workers.md %}) — the local (default) execution backend
