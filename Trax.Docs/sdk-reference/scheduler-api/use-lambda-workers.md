---
title: UseLambdaWorkers
description: Reference for UseLambdaWorkers, which routes chosen trains' queued runs to an AWS Lambda function by direct invocation, with retry and routing options.
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 14
package: Trax.Scheduler.Lambda
namespace:
  - Trax.Scheduler.Lambda.Extensions
  - Trax.Scheduler.Lambda.Configuration
source:
  - Trax.Scheduler/src/Trax.Scheduler.Lambda/Extensions/LambdaSchedulerExtensions.cs
  - Trax.Scheduler/src/Trax.Scheduler.Lambda/Configuration/LambdaWorkerOptions.cs
  - Trax.Scheduler/src/Trax.Scheduler.Lambda/Configuration/LambdaRetryOptions.cs
  - Trax.Scheduler/src/Trax.Scheduler.Lambda/Services/LambdaJobSubmitter.cs
---

# UseLambdaWorkers

Routes chosen trains' queued runs to an AWS Lambda function by direct SDK invocation. No public
endpoint is involved; access is governed by IAM. Trains not routed here keep running on the local
workers.

## Signature

```csharp fragment
public static SchedulerConfigurationBuilder UseLambdaWorkers(
    this SchedulerConfigurationBuilder builder,
    Action<LambdaWorkerOptions> configure,
    Action<SubmitterRouting>? routing = null
)
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `configure` | `Action<LambdaWorkerOptions>` | Sets the function name, client and retry options |
| `routing` | `Action<SubmitterRouting>?` | Names the trains sent to this function with `ForTrain<TTrain>()`, which returns the routing for chaining. When omitted, no train is routed here by name; `[TraxRemote]` trains are, if this is the first `UseRemoteWorkers`, `UseSqsWorkers` or `UseLambdaWorkers` call. With none of the three, a `[TraxRemote]` train fails the scheduler's build |

### LambdaWorkerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FunctionName` | `string` | required | The function name, ARN, or partial ARN to invoke |
| `ConfigureLambdaClient` | `Action<AmazonLambdaConfig>?` | `null` | Configures the `AmazonLambdaConfig` the client is built from: region, a LocalStack `ServiceURL`, and so on |
| `Retry` | `LambdaRetryOptions` | see below | Retries for transient AWS failures |
| `SigningKey` | `byte[]?` | `null` | The key shared with the function's `ConfigureRunner`, at least 32 bytes. When set, each envelope carries a `Signature` over its `PayloadJson`. See [Authorization Posture](/docs/scheduler/remote-execution#authorization-posture) |

### LambdaRetryOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRetries` | `int` | 5 | Retry attempts; 0 disables retrying |
| `BaseDelay` | `TimeSpan` | 1 second | The first delay, doubled on each attempt with plus or minus 25% jitter |
| `MaxDelay` | `TimeSpan` | 30 seconds | The cap on any one delay |

Retried: AWS status codes 429, 502, 503 and 504, and a network-level `HttpRequestException`. Not
retried: `ResourceNotFoundException`, `InvalidParameterValueException` and function errors.

## Returns

`SchedulerConfigurationBuilder`, for chaining.

## Behavior

- A routed entry is serialized as a `RemoteJobRequest` (the metadata id and the input, if any),
  wrapped in a `LambdaEnvelope` with `Type = Execute`, and sent with `InvocationType.Event`. A
  `FunctionError` on the response throws `TrainException`. The job id is `"lambda-{guid}"`.
- A train can be routed to one submitter only. Routing it to two throws `InvalidOperationException`
  when the scheduler is built.
- `UseLambdaWorkers()` may be called once per function. Each call keeps its own
  `LambdaWorkerOptions` and Lambda client and invokes only the trains it routes.
- An asynchronous invocation payload is at most 256 KB.
- [`TraxLambdaFunction`](/docs/sdk-reference/scheduler-api/trax-lambda-function) cancels a job
  `TerminalWriteMargin` before the function's own timeout, so the run can record its outcome.
- A cancel reaches the function through the database, as for
  [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run#behavior).
- The scheduler process needs `lambda:InvokeFunction` on the function.

### Registered services

| Service | Lifetime | Description |
|---------|----------|-------------|
| `IAmazonLambda` (keyed) | Singleton | The call's own client, configured by its `ConfigureLambdaClient` |
| Lambda job submitter | Created per dispatch | An internal `IJobSubmitter` the JobDispatcher creates for each train routed to this call |
| `LambdaWorkerOptions`, `IAmazonLambda` | Singleton | The first call's options and client, also registered by type (`UseLambdaRun()` registers its own `IAmazonLambda` too) |

The default `IJobSubmitter` is not replaced: local workers keep running the trains not routed here.

## Example

```csharp compile
using Trax.Scheduler.Lambda.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(assembly)
    .AddScheduler(scheduler => scheduler
        .UseLambdaWorkers(
            lambda =>
            {
                lambda.FunctionName = "content-shield-runner";
                lambda.Retry.MaxRetries = 3;
            },
            routing => routing
                .ForTrain<IReviewContentTrain>()
                .ForTrain<ISendViolationNoticeTrain>())
        .Schedule<ISyncTrain>("sync", new SyncInput(), Every.Minutes(5))));
```

`IReviewContentTrain` and `ISendViolationNoticeTrain` run on the function; `ISyncTrain` runs on the
local workers.

## See also

- [Run trains on AWS Lambda](/docs/scheduler/run-trains-on-lambda)
- [How remote dispatch works](/docs/scheduler/how-remote-dispatch-works)
- [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run), synchronous runs on Lambda
- [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function)
- [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) and [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers), the other transports
