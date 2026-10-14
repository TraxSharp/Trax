---
title: UseLambdaRun
description: Reference for UseLambdaRun, which sends synchronous run requests to an AWS Lambda function by direct SDK invocation, with LambdaRunOptions.
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 15
package: Trax.Scheduler.Lambda
namespace:
  - Trax.Scheduler.Lambda.Extensions
  - Trax.Scheduler.Lambda.Configuration
source:
  - Trax.Scheduler/src/Trax.Scheduler.Lambda/Extensions/LambdaSchedulerExtensions.cs
  - Trax.Scheduler/src/Trax.Scheduler.Lambda/Configuration/LambdaRunOptions.cs
  - Trax.Scheduler/src/Trax.Scheduler.Lambda/Services/LambdaRunExecutor.cs
---

# UseLambdaRun

Sends synchronous `run` requests (`ITrainExecutionService.RunAsync`, and the GraphQL `run*`
mutations through it) to an AWS Lambda function by direct SDK invocation instead of running the
train in process. The call blocks until the function returns and gives back the train's output. No
public endpoint is involved; access is governed by IAM.

## Signature

```csharp fragment
public static SchedulerConfigurationBuilder UseLambdaRun(
    this SchedulerConfigurationBuilder builder,
    Action<LambdaRunOptions> configure
)
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `configure` | `Action<LambdaRunOptions>` | Sets the function name and client options |

### LambdaRunOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FunctionName` | `string` | required | The function name, ARN, or partial ARN to invoke |
| `ConfigureLambdaClient` | `Action<AmazonLambdaConfig>?` | `null` | Configures the `AmazonLambdaConfig` the client is built from: region, a LocalStack `ServiceURL`, and so on |
| `Retry` | [`LambdaRetryOptions`](/docs/sdk-reference/scheduler-api/use-lambda-workers#lambdaretryoptions) | 5 retries, 1 second base, 30 seconds cap | Retries for transient invocation failures |
| `SigningKey` | `byte[]?` | `null` | The key shared with the function's `ConfigureRunner`, at least 32 bytes. When set, each envelope carries a `Signature` over its `PayloadJson`. See [Authorization Posture](/docs/scheduler/remote-execution#authorization-posture) |

## Returns

`SchedulerConfigurationBuilder`, for chaining.

## Behavior

- Each run is serialized as a `RemoteRunRequest` (train name, input JSON, input type), wrapped in
  a `LambdaEnvelope` with `Type = Run`, and sent with `InvocationType.RequestResponse`.
- A `FunctionError` on the response throws `RemoteRunException` (a `TrainException`) with no
  public message. A `RemoteRunResponse` with `IsError` set throws `RemoteRunException` carrying the
  remote error details and the runner's `PublicMessage`. Otherwise the train's output is returned.
- The function's own limits apply: a synchronous response payload is at most 6 MB and a function
  runs for at most 15 minutes.
- A cancel reaches the function through the database: it sets the run's cancel flag, which the
  train sees at its next junction boundary when the function registers `CancellationCheckProvider`
  (through `AddJunctionProgress()`). A junction already running is not interrupted.
- The scheduler process needs `lambda:InvokeFunction` on the function.

### Registered services

| Service | Lifetime | Description |
|---------|----------|-------------|
| `LambdaRunOptions` | Singleton | The options |
| `IAmazonLambda` | Singleton | The Lambda client |
| `IRunExecutor` | Scoped | Replaces the default `LocalRunExecutor` with one that invokes the function and waits for the response |

## Example

```csharp compile
using Trax.Scheduler.Lambda.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(assembly)
    .AddScheduler(scheduler => scheduler
        .UseLambdaWorkers(
            lambda => lambda.FunctionName = "content-shield-runner",
            routing => routing.ForTrain<IReviewContentTrain>())
        .UseLambdaRun(lambda =>
        {
            lambda.FunctionName = "content-shield-runner";
            lambda.Retry.MaxRetries = 3;
        })));
```

## See also

- [Run trains on AWS Lambda](/docs/scheduler/run-trains-on-lambda), setting up the function and the scheduler
- [How remote dispatch works](/docs/scheduler/how-remote-dispatch-works), what happens to a run on the way to the function
- [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers), queued runs on Lambda
- [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function), the function's base class
- [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run), the same over HTTP
