---
layout: default
title: AddTraxJobRunner
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 10
---

# AddTraxJobRunner

Registers the minimal services needed to run `JobRunnerTrain` without the full scheduler. Used on the **remote receiver side**, the process that actually executes trains dispatched by a scheduler via `UseRemoteWorkers()`. Also documents `UseTraxRunEndpoint()` for handling synchronous `run` requests from `UseRemoteRun()`.

## Signatures

```csharp
public static IServiceCollection AddTraxJobRunner(
    this IServiceCollection services,
    Action<TraxJobRunnerOptions> configure
)

public static IServiceCollection AddTraxJobRunner(
    this IServiceCollection services
)
```

```csharp
public static RouteHandlerBuilder UseTraxJobRunner(
    this IEndpointRouteBuilder app,
    string route = "/trax/execute"
)
```

```csharp
public static RouteHandlerBuilder UseTraxRunEndpoint(
    this IEndpointRouteBuilder app,
    string route = "/trax/run"
)
```

## Parameters

### AddTraxJobRunner

| Parameter | Type | Description |
|-----------|------|-------------|
| `configure` | `Action<TraxJobRunnerOptions>` | Sets the runner's authorization posture. Every runner entry point refuses to start without one |

The overload without `configure` registers the execution pipeline with no posture. It suits a host that needs `ITraxScheduler` without mapping a runner endpoint; mapping one afterwards fails at startup.

### TraxJobRunnerOptions

| Property / method | Type | Default | Description |
|-------------------|------|---------|-------------|
| `SigningKey` | `byte[]?` | `null` | The key shared with the scheduler's `SigningKey`, at least 32 bytes. When set, every request must carry a valid `Trax-Signature` |
| `AuthorizationPolicy` | `string?` | `null` | An ASP.NET authorization policy applied to `UseTraxJobRunner` and `UseTraxRunEndpoint`. Must admit only the scheduler. Not applicable to SQS or Lambda |
| `MaxRequestBodyBytes` | `long` | 8 MiB (`DefaultMaxRequestBodyBytes`) | The largest request body the HTTP entry points (`UseTraxJobRunner`, `UseTraxRunEndpoint`, and `TraxLambdaFunction`'s local routes) read; a larger one gets `413`. The default holds a train input at the mediator's default stored-input cap; raise it together with the scheduler's `MaxInputJsonBytes`. Must be positive |
| `MaxClockSkew` | `TimeSpan` | 5 minutes | How far a signed request's timestamp may be from the runner's clock, and how long a nonce is remembered |
| `AllowUnsignedRequests()` | method | | Accepts requests with no signature. Each entry point logs a warning when it starts |
| `UseInMemoryNonceStore()` | method | | Keeps accepted nonces in this process instead of the database. Only for a runner that runs as one instance: each instance keeps its own |

See [Authorization Posture](/docs/scheduler/remote-execution#authorization-posture) for how the three combine and what each transport checks.

### UseTraxJobRunner

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `route` | `string` | `"/trax/execute"` | The route to map the POST endpoint for queued jobs |

### UseTraxRunEndpoint

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `route` | `string` | `"/trax/run"` | The route to map the POST endpoint for synchronous run requests |

## Examples

### Minimal Remote Executor (Queue Only)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(MyTrain).Assembly)
);
builder.Services.AddTraxJobRunner(runner =>
    runner.SigningKey = Convert.FromBase64String(builder.Configuration["Trax:RunnerSigningKey"]!)
);

var app = builder.Build();
app.UseTraxJobRunner("/trax/execute");
app.Run();
```

### Remote Executor (Queue + Run)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
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

Both endpoints need `AddTraxJobRunner(runner => ...)`: they resolve `ITraxRequestHandler` from it, and read the posture from its options while mapping.

### With an Authorization Policy

Name the policy in the runner options. The runner applies it to both endpoints and checks at startup that a posture exists, which it cannot do for a `.RequireAuthorization()` chained on afterwards. The policy must admit only the scheduler, because the runner runs what it is sent without per-train authorization:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(MyTrain).Assembly)
);
builder.Services.AddAuthentication(/* your scheme */);
builder.Services.AddAuthorization(o =>
    o.AddPolicy("trax-scheduler", p => p.RequireClaim("scope", "trax:dispatch")));
builder.Services.AddTraxJobRunner(runner => runner.AuthorizationPolicy = "trax-scheduler");

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxJobRunner("/trax/execute");
app.Run();
```

### AWS Lambda (via TraxLambdaFunction base class)

For Lambda deployments, use the `Trax.Runner.Lambda` package which provides a base class that handles service provider lifecycle, request routing, and cancellation:

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

        services.AddTrax(trax => trax
            .AddEffects(effects => effects.UsePostgres(connString))
            .AddMediator(typeof(MyTrain).Assembly));
    }

    protected override void ConfigureRunner(TraxJobRunnerOptions runner, IConfiguration configuration) =>
        runner.SigningKey = Convert.FromBase64String(configuration["Trax:RunnerSigningKey"]!);
}
```

See the [TraxLambdaFunction API reference](/docs/sdk-reference/scheduler-api/trax-lambda-function) for details.

## What It Registers

### AddTraxJobRunner

Registers the minimum set of services to run `JobRunnerTrain`:

| Service | Lifetime | Description |
|---------|----------|-------------|
| `SchedulerConfiguration` | Singleton | Empty configuration (no manifests, no polling) |
| `ICancellationRegistry` → `CancellationRegistry` | Singleton | Process-local cancellation tracking |
| `ITraxScheduler` → `TraxScheduler` | Scoped | Runtime scheduler interface |
| `IDormantDependentContext` → `DormantDependentContext` | Scoped | Dependent train context |
| `IJobRunnerTrain` → `JobRunnerTrain` | Scoped | The train execution pipeline |
| `ITraxRequestHandler` → `TraxRequestHandler` | Scoped | The execution step behind the runner's entry points: reads a request's input, resolves the train and runs it inside a trusted scope. It checks no posture and verifies no signature, so call it only from an entry point that has already verified the request; the entry points Trax ships do. Hidden from IntelliSense |
| `TraxJobRunnerOptions` | Singleton | The posture passed to `configure` |
| `INonceStore` | Singleton | Where a signing runner records accepted nonces: the `runner_nonce` table through `IDataContext.RunnerNonces`, shared by every instance on the database, or memory after `UseInMemoryNonceStore()`. Registered with `TryAdd`, so a host's own store replaces it. With a `SigningKey` and neither a relational data provider nor `UseInMemoryNonceStore()`, resolving it fails at startup |
| `RunnerRequestVerifier` | Singleton | Checks the posture at startup and each request's signature and freshness |

**Not registered:** ManifestManager, JobDispatcher, polling services, startup service, `LocalWorkerService`. This process only runs trains; it doesn't schedule or dispatch them.

The `SchedulerConfiguration`, `ICancellationRegistry`, `ITraxScheduler` and `IDormantDependentContext` registrations use `TryAdd`, and `AddScheduler` replaces the configuration and cancellation registry. A scheduler host that also maps a runner endpoint for another scheduler therefore keeps the configuration it built, whichever of `AddTraxJobRunner` and `AddScheduler` it calls first.

### UseTraxJobRunner

Throws while mapping when the runner options have no posture. Otherwise maps a `POST` endpoint at the specified route that:

1. Returns `415` for a body that is not JSON. With a `SigningKey`, returns `401` for a `Trax-Signature` header that is missing, malformed or stale before any of the body is read, `413` for a body over `MaxRequestBodyBytes`, and `401` for a signature the body does not match or a replayed nonce. Refusals are logged as a warning at most once a minute, with a count of those since the last warning, and at Debug otherwise
2. Reads a `RemoteJobRequest` from the request body. A body that repeats a property (in any case), does not parse, or is `null` gets `400 Bad Request`. The host's global JSON options are not used for this
3. Deserializes the input (if present) into the registered train input type whose fully-qualified name the request gives. A name no registered train takes is refused
4. Calls `IJobRunnerTrain.Run(new RunJobRequest(metadataId, input))`, which refuses a metadata row that is not `Pending` or belongs to a different train than the input's
5. Returns `200 OK` with a `RemoteJobResponse` containing the metadata ID on success
6. On error: logs the exception, then returns `200 OK` with `RemoteJobResponse.IsError = true`, the `ExceptionType`, and the message of a `TrainException` (any other exception gets a fixed message). No stack trace is returned

With an `AuthorizationPolicy`, the endpoint requires it.

### UseTraxRunEndpoint

Enforces the same posture as `UseTraxJobRunner`, and throws while mapping without one. Maps a `POST` endpoint at the specified route that handles synchronous run requests from [`UseRemoteRun()`](/docs/sdk-reference/scheduler-api/use-remote-run):

1. Reads a `RemoteRunRequest` from the request body (contains train name and input JSON), after the same `415`, `401` and `413` checks, and with the same `400` refusals
2. Refuses a train name that is one of the scheduler's own trains (ManifestManager, JobDispatcher, JobRunner, MetadataCleanup, DeadLetterCleanup), by full name, or by short name unless a host train shares it
3. Resolves `ITrainExecutionService` and calls `RunAsync(trainName, inputJson)`
4. Serializes the train output as JSON
5. Returns `200 OK` with a `RemoteRunResponse` containing the metadata ID, output JSON, and output type
6. On error: returns `200 OK` with `RemoteRunResponse.IsError = true` and structured error fields (`ErrorMessage`, `ExceptionType`, `FailureJunction`, `FailureClass`). `StackTrace` is always null, and `ErrorMessage` is fixed text unless the failure is a `TrainException`. `FailureClass` is the classification this process's `IFailureClassifier` assigned, or null when none is registered or it did not recognise the failure. `PublicMessage` is the message of a plain `TrainException` and null for anything else, including a type derived from `TrainException`. The response is written with Trax's own JSON options (enums as integers), not the host's, so a host configured to write enums as strings does not change the wire. The reading side also accepts names, and reads a class it does not know as `Unclassified`. Uses in-band errors to distinguish from infrastructure failures

## Shared Requirements

The remote process must:

- **Reference the same train assemblies** passed to `AddMediator()`. Input types are matched by fully-qualified name against the registered trains' input types.
- **Connect to the same Postgres database.** Metadata, manifests, and state are shared across all processes.
- **Register the effect system.** `AddTrax()` with `UsePostgres()` is required.

## Package

```
dotnet add package Trax.Scheduler
```

## See Also

- [Remote Execution](/docs/scheduler/remote-execution): architecture overview and deployment models
- [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers): scheduler-side configuration for HTTP dispatch (queue path)
- [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run): scheduler-side configuration for remote run execution
- [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker): standalone worker (poll-based alternative)
