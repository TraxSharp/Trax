---
title: AddTraxJobRunner
description: Reference for AddTraxJobRunner, UseTraxJobRunner and UseTraxRunEndpoint, which set up a remote process that executes trains a scheduler dispatches to it.
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 12
package: Trax.Scheduler
namespace:
  - Trax.Scheduler.Extensions
  - Trax.Scheduler.Configuration
source:
  - Trax.Scheduler/src/Trax.Scheduler/Extensions/JobRunnerExtensions.cs
  - Trax.Scheduler/src/Trax.Scheduler/Configuration/TraxJobRunnerOptions.cs
---

# AddTraxJobRunner

`AddTraxJobRunner` registers what a runner needs to execute `JobRunnerTrain` without the full scheduler: the process that runs the trains a scheduler sends it through `UseRemoteWorkers()`, `UseSqsWorkers()` or `UseLambdaWorkers()`. `UseTraxJobRunner` maps its endpoint for queued jobs, and `UseTraxRunEndpoint` its endpoint for synchronous runs from `UseRemoteRun()`. The three are in `JobRunnerExtensions`.

## Signatures

```csharp fragment
public static IServiceCollection AddTraxJobRunner(
    this IServiceCollection services,
    Action<TraxJobRunnerOptions> configure
)

public static IServiceCollection AddTraxJobRunner(
    this IServiceCollection services
)

public static RouteHandlerBuilder UseTraxJobRunner(
    this IEndpointRouteBuilder app,
    string route = "/trax/execute"
)

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
| `AllowUnsignedRequests()` | method | | Accepts requests with no signature. Each entry point logs a warning when it starts. Returns the options |
| `UnsignedRequestsAllowed` | `bool` | `false` | Whether `AllowUnsignedRequests()` was called |
| `UseInMemoryNonceStore()` | method | | Keeps accepted nonces in this process instead of the database. Only for a runner that runs as one instance: each instance keeps its own. Returns the options |
| `InMemoryNonceStore` | `bool` | `false` | Whether `UseInMemoryNonceStore()` was called |

See [Authorization Posture](/docs/scheduler/remote-execution#authorization-posture) for how the three combine and what each transport checks.

### UseTraxJobRunner

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `route` | `string` | `"/trax/execute"` | The route to map the POST endpoint for queued jobs |

### UseTraxRunEndpoint

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `route` | `string` | `"/trax/run"` | The route to map the POST endpoint for synchronous run requests |

## Returns

`AddTraxJobRunner` returns the `IServiceCollection`; `UseTraxJobRunner` and `UseTraxRunEndpoint` return the endpoint's `RouteHandlerBuilder`.

## Behavior

### Registered services

`AddTraxJobRunner` registers the minimum set of services to run `JobRunnerTrain`:

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
| A decision-recording startup check | Hosted service | Refuses to start the host, naming the trains, when trains that ask a decider run on a host without `AddDecisionRecording()`. Runs as the host starts, before any worker claims work. Added once however many of `AddScheduler`, `AddTraxJobRunner` and `AddTraxWorker` a host calls. See [A host that does not record](/docs/effect/decisions#a-host-that-does-not-record) |

**Not registered:** ManifestManager, JobDispatcher, polling services, startup service, `LocalWorkerService`. This process only runs trains; it doesn't schedule or dispatch them.

The `SchedulerConfiguration`, `ICancellationRegistry`, `ITraxScheduler` and `IDormantDependentContext` registrations use `TryAdd`, and `AddScheduler` replaces the configuration and cancellation registry. A scheduler host that also maps a runner endpoint for another scheduler therefore keeps the configuration it built, whichever of `AddTraxJobRunner` and `AddScheduler` it calls first.

### What UseTraxJobRunner maps

Throws while mapping when the runner options have no posture. Otherwise maps a `POST` endpoint at the specified route that:

1. Returns `415` for a body that is not JSON. With a `SigningKey`, returns `401` for a `Trax-Signature` header that is missing, malformed or stale before any of the body is read, `413` for a body over `MaxRequestBodyBytes`, and `401` for a signature the body does not match or a replayed nonce. Refusals are logged as a warning at most once a minute, with a count of those since the last warning, and at Debug otherwise
2. Reads a `RemoteJobRequest` from the request body. A body that repeats a property (in any case), does not parse, or is `null` gets `400 Bad Request`. The host's global JSON options are not used for this
3. Deserializes the input (if present) into the registered train input type whose fully-qualified name the request gives. A name no registered train takes is refused
4. Calls `IJobRunnerTrain.Run(new RunJobRequest(metadataId, input))`, which refuses a metadata row that is not `Pending` or belongs to a different train than the input's
5. Returns `200 OK` with a `RemoteJobResponse` containing the metadata ID on success
6. On error: logs the exception, then returns `200 OK` with `RemoteJobResponse.IsError = true`, the `ExceptionType`, and the message of a `TrainException` (any other exception gets a fixed message). No stack trace is returned

With an `AuthorizationPolicy`, the endpoint requires it.

### What UseTraxRunEndpoint maps

Enforces the same posture as `UseTraxJobRunner`, and throws while mapping without one. Maps a `POST` endpoint at the specified route that handles synchronous run requests from [`UseRemoteRun()`](/docs/sdk-reference/scheduler-api/use-remote-run):

1. Reads a `RemoteRunRequest` from the request body (contains train name and input JSON), after the same `415`, `401` and `413` checks, and with the same `400` refusals
2. Refuses a train name that is one of the scheduler's own trains (ManifestManager, JobDispatcher, JobRunner, MetadataCleanup, DeadLetterCleanup), by full name, or by short name unless a host train shares it
3. Resolves `ITrainExecutionService` and calls `RunAsync(trainName, inputJson)`
4. Serializes the train output as JSON
5. Returns `200 OK` with a `RemoteRunResponse` containing the metadata ID, output JSON, and output type
6. On error: returns `200 OK` with `RemoteRunResponse.IsError = true` and structured error fields (`ErrorMessage`, `ExceptionType`, `FailureJunction`, `FailureClass`). `StackTrace` is always null, and `ErrorMessage` is fixed text unless the failure is a `TrainException`. `FailureClass` is the classification this process's `IFailureClassifier` assigned, or null when none is registered or it did not recognise the failure. `PublicMessage` is the message of a plain `TrainException` and null for anything else, including a type derived from `TrainException`. The response is written with Trax's own JSON options (enums as integers), not the host's, so a host configured to write enums as strings does not change the wire. The reading side also accepts names, and reads a class it does not know as `Unclassified`. Uses in-band errors to distinguish from infrastructure failures

### Shared requirements

A runner process:

- References the train assemblies the scheduler passes to `AddMediator()`. Input types are matched by fully-qualified name against the registered trains' input types.
- Connects to the scheduler's database. Metadata, manifests, and state are shared across all processes.
- Registers the effects with `AddTrax()` and a data provider such as `UsePostgres()`.

### Infrastructure types

Public because the runner and scheduler packages ship separately and share them; hidden from IntelliSense where marked.

| Type or member | Description |
|----------------|-------------|
| `RunnerRequestSignature` | Signs a runner request and checks a signature. The value is `v1,t=<unix seconds>,n=<nonce>,s=<base64 HMAC-SHA256>` in the `Trax-Signature` header, a Lambda envelope's `Signature`, or an SQS message attribute |
| `RunnerRequestSignature.MinimumKeyLength` | `32`: the shortest signing key accepted, in bytes |
| `RunnerRequestSignature.EnsureKey(key, paramName)` | Throws when the key is missing or shorter than `MinimumKeyLength` |
| `RunnerRequestPurpose` | What a signed request is for, part of the signed text: `Execute` (a queued job) or `Run` (a synchronous run) |
| `RunnerRequestVerifier` | The singleton that checks the posture at startup and each request's signature |
| `RunnerRequestVerifier.RequiresSignature` | True when a `SigningKey` is configured |
| `RunnerRequestVerifier.CheckHeader(signature, requireFresh)` | The checks that need no body: present, well formed and, with `requireFresh`, within `MaxClockSkew`. `Accepted` when no signing key is configured |
| `RunnerRequestVerifier.ReadVerifiedBodyAsync(request, purpose)` | Reads an HTTP request's body and verifies it: the body, or the status (`401`, `413`) to answer with |
| `RunnerRequestVerifier.EnsurePosture(entryPoint)` | Throws `InvalidOperationException` for an entry point with neither a signing key nor `AllowUnsignedRequests` |
| `RunnerRequestVerifier.ReportRefusal(entryPoint, reason)` | Logs a refused request, as a warning at most once a minute and at Debug otherwise |
| `RunnerRequestVerdict` | A verification's outcome: `Accepted`, `Missing`, `Invalid`, `Stale` or `Replayed` |
| `INonceStore.TryRecordAsync(nonce, expiresAt, now, cancellationToken)` | Records a nonce unless an unexpired record of it exists; true when this call recorded it |
| `ExecuteJobResult` | `ITraxRequestHandler.ExecuteJobAsync`'s result: the `MetadataId` of the run |
| `RemoteRunJson` | The fixed JSON options of the remote-run wire, independent of the host's |
| `RemoteRunOutput` | Reads a remote run's output into the declared output type on the calling side |
| `RemoteRunResponse.ToTrainException()` | The `RemoteRunException` the calling side records for an error response, carrying the worker's structured failure fields and `PublicMessage` |

## Examples

```csharp compile
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(ISyncTrain).Assembly)
);
builder.Services.AddTraxJobRunner(runner =>
    runner.SigningKey = Convert.FromBase64String(builder.Configuration["Trax:RunnerSigningKey"]!)
);

var app = builder.Build();
app.UseTraxJobRunner("/trax/execute");
app.Run();
```

A runner that serves both paths:

```csharp compile
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(ISyncTrain).Assembly)
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

Name the policy in the runner options. The runner applies it to both endpoints and checks at startup that a posture exists, which it cannot do for a `.RequireAuthorization()` chained on afterwards. The policy must admit only the scheduler, because the runner runs what it is sent without per-train authorization:

```csharp compile
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(typeof(ISyncTrain).Assembly)
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

A Lambda runner derives from [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function), which calls `AddTraxJobRunner` itself.

## See also

- [How remote dispatch works](/docs/scheduler/how-remote-dispatch-works)
- [Remote Execution](/docs/scheduler/remote-execution): choosing where trains run
- [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers): scheduler-side configuration for HTTP dispatch (queue path)
- [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run): scheduler-side configuration for remote run execution
- [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker): standalone worker (poll-based alternative)
