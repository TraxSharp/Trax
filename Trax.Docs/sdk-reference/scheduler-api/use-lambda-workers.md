---
layout: default
title: UseLambdaWorkers
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 10.1
---

# UseLambdaWorkers

Routes specific trains to an AWS Lambda function for execution via direct SDK invocation. No public endpoint is created; access is governed entirely by IAM policies. Trains not included in the routing configuration continue to execute locally via `PostgresJobSubmitter` and `LocalWorkerService`.

## Package

```
dotnet add package Trax.Scheduler.Lambda
```

## Signature

```csharp
public static SchedulerConfigurationBuilder UseLambdaWorkers(
    this SchedulerConfigurationBuilder builder,
    Action<LambdaWorkerOptions> configure,
    Action<SubmitterRouting>? routing = null
)
```

Defined in `Trax.Scheduler.Lambda.Extensions.LambdaSchedulerExtensions`.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configure` | `Action<LambdaWorkerOptions>` | Yes | Callback to set the Lambda function name and client options |
| `routing` | `Action<SubmitterRouting>?` | No | Callback to specify which trains should be dispatched to this Lambda function. When omitted, no train is routed here explicitly; `[TraxRemote]`-attributed trains are, if this is the first `UseRemoteWorkers`, `UseSqsWorkers` or `UseLambdaWorkers` call. With none of the three, a `[TraxRemote]` train fails the scheduler's build. |

## Returns

`SchedulerConfigurationBuilder`, for continued fluent chaining.

## LambdaWorkerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FunctionName` | `string` | _(required)_ | The Lambda function name, ARN, or partial ARN to invoke |
| `ConfigureLambdaClient` | `Action<AmazonLambdaConfig>?` | `null` | Optional callback to configure the `AmazonLambdaConfig` (set region, endpoint override for LocalStack, etc.) |
| `Retry` | `LambdaRetryOptions` | _(see below)_ | Retry options for transient AWS failures (429, 502, 503, 504) |
| `SigningKey` | `byte[]?` | `null` | The key shared with the function's `ConfigureRunner`, at least 32 bytes. When set, each envelope carries a `Signature` over its `PayloadJson`. See [Authorization Posture](/docs/scheduler/remote-execution#authorization-posture) |

### LambdaRetryOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRetries` | `int` | 5 | Maximum retry attempts. Set to 0 to disable retries. |
| `BaseDelay` | `TimeSpan` | 1 second | Starting delay between retries (doubled on each attempt with ±25% jitter) |
| `MaxDelay` | `TimeSpan` | 30 seconds | Maximum delay cap to prevent unbounded exponential growth |

Retries on AWS status codes 429 (Throttling), 502 (Bad Gateway), 503 (Service Unavailable), and 504 (Gateway Timeout), as well as network-level `HttpRequestException`. Does not retry on `ResourceNotFoundException`, `InvalidParameterValueException`, or Lambda function errors.

## SubmitterRouting

| Method | Description |
|--------|-------------|
| `ForTrain<TTrain>()` | Routes the specified train type to this Lambda function. Returns the routing instance for chaining. |

## Examples

### Basic Usage

```csharp
using Trax.Scheduler.Lambda.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(assemblies)
    .AddScheduler(scheduler => scheduler
        .UseLambdaWorkers(
            lambda => lambda.FunctionName = "content-shield-runner",
            routing => routing
                .ForTrain<IReviewContentTrain>()
                .ForTrain<ISendViolationNoticeTrain>())
        .Schedule<IMyTrain>("my-job", new MyInput(), Every.Minutes(5))
    )
);
```

In this example, `IReviewContentTrain` and `ISendViolationNoticeTrain` are dispatched to Lambda. `IMyTrain` executes locally via the default `PostgresJobSubmitter`.

### With Custom Region

```csharp
.UseLambdaWorkers(
    lambda =>
    {
        lambda.FunctionName = "content-shield-runner";
        lambda.ConfigureLambdaClient = config =>
            config.RegionEndpoint = Amazon.RegionEndpoint.EUWest1;
    },
    routing => routing.ForTrain<IReviewContentTrain>())
```

### With LocalStack (Development)

```csharp
.UseLambdaWorkers(
    lambda =>
    {
        lambda.FunctionName = "content-shield-runner";
        lambda.ConfigureLambdaClient = config =>
            config.ServiceURL = "http://localhost:4566";
    },
    routing => routing.ForTrain<IReviewContentTrain>())
```

### With Lambda Run (Full Offload)

Combine `UseLambdaWorkers` with `UseLambdaRun` to offload both queued and synchronous trains to Lambda:

```csharp
.AddScheduler(scheduler => scheduler
    .UseLambdaWorkers(
        lambda => lambda.FunctionName = "content-shield-runner",
        routing => routing
            .ForTrain<IReviewContentTrain>()
            .ForTrain<ISendViolationNoticeTrain>())
    .UseLambdaRun(lambda => lambda.FunctionName = "content-shield-runner")
)
```

### Mixed with Other Transports

You can use Lambda workers alongside HTTP remote workers and SQS workers, each for different trains:

```csharp
.AddScheduler(scheduler => scheduler
    .UseLambdaWorkers(
        lambda => lambda.FunctionName = "content-shield-runner",
        routing => routing.ForTrain<IReviewContentTrain>())
    .UseRemoteWorkers(
        remote => remote.BaseUrl = "https://gpu-workers/trax/execute",
        routing => routing.ForTrain<IAiInferenceTrain>())
    .UseSqsWorkers(
        sqs => sqs.QueueUrl = "https://sqs.../trax-jobs",
        routing => routing.ForTrain<IBatchProcessTrain>())
)
```

Each train can only be routed to one submitter. Routing the same train to multiple submitters throws `InvalidOperationException` at build time.

`UseLambdaWorkers()` can also be called more than once, one call per function. Each call keeps its own `LambdaWorkerOptions` and its own Lambda client, and invokes only the trains it routes.

## How It Works

When the JobDispatcher processes a work queue entry, it checks whether the entry's train is routed to this function. If it is, the Lambda submitter:

1. Serializes a `RemoteJobRequest` containing the metadata ID and optional input
2. Wraps it in a `LambdaEnvelope` with `Type = Execute`
3. Calls `IAmazonLambda.InvokeAsync()` with `InvocationType.Event` (fire-and-forget)
4. Checks `response.FunctionError` and throws `TrainException` if the Lambda failed
5. Returns a synthetic job ID (`"lambda-{guid}"`)

The Lambda function receives the `LambdaEnvelope` via `TraxLambdaFunction.FunctionHandler()`, deserializes the `RemoteJobRequest`, and executes the train through `ITraxRequestHandler.ExecuteJobAsync()`.

## IAM Permissions

The scheduler process needs:

```json
{
  "Effect": "Allow",
  "Action": "lambda:InvokeFunction",
  "Resource": "arn:aws:lambda:us-east-1:123456789012:function:content-shield-runner"
}
```

## Registered Services

`UseLambdaWorkers()` registers:

| Service | Lifetime | Description |
|---------|----------|-------------|
| `IAmazonLambda` (keyed) | Singleton | The call's own Lambda client, configured by its `ConfigureLambdaClient` |
| Lambda job submitter | Created per dispatch | An internal `IJobSubmitter` that invokes the call's function with its own options and client. The JobDispatcher creates it for each train routed to this call; application code does not resolve it |
| `LambdaWorkerOptions`, `IAmazonLambda` | Singleton | The **first** call's options and client, also registered by type, as before (`UseLambdaRun()` registers its own `IAmazonLambda` too) |

> **Note:** `UseLambdaWorkers()` does **not** replace the default `IJobSubmitter`. Local workers continue to run for trains not routed to this function.

## Limitations

- **Payload size limit:** Lambda invocation payloads are limited to 256 KB. If your serialized train input exceeds this, store the data externally and pass a reference.
- **Function timeout:** `TraxLambdaFunction` cancels a job before the function's own timeout, holding back `TerminalWriteMargin` (5 seconds) or half the time left, whichever is smaller, so the run can record its outcome. Give the function a timeout comfortably longer than your longest job plus that margin; at or below the margin (AWS's default three seconds) jobs still run, with a warning logged once per instance, and a job out of time before it starts is recorded `Cancelled`. See [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function).
- **Cancellation reaches the function through the database:** Dashboard "Cancel" sets the run's cancel flag, which the train sees at its next junction boundary when the function registers `CancellationCheckProvider` (via `AddJunctionProgress()`). A junction already running is not interrupted. See [Remote Execution](/docs/scheduler/remote-execution#limitations).

## See Also

- [Remote Execution](/docs/scheduler/remote-execution): architecture overview and deployment models
- [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run): offload synchronous runs to Lambda
- [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function): the Lambda receiver base class
- [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers): HTTP-based per-train remote dispatch (alternative transport)
- [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers): SQS-based per-train dispatch (alternative transport)
