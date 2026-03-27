---
layout: default
title: UseLambdaWorkers
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 10.1
---

# UseLambdaWorkers

Routes specific trains to an AWS Lambda function for execution via direct SDK invocation. No public endpoint is created — access is governed entirely by IAM policies. Trains not included in the routing configuration continue to execute locally via `PostgresJobSubmitter` and `LocalWorkerService`.

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
| `routing` | `Action<SubmitterRouting>?` | No | Callback to specify which trains should be dispatched to this Lambda function. When omitted, only `[TraxRemote]`-attributed trains are routed. |

## Returns

`SchedulerConfigurationBuilder` — for continued fluent chaining.

## LambdaWorkerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FunctionName` | `string` | _(required)_ | The Lambda function name, ARN, or partial ARN to invoke |
| `ConfigureLambdaClient` | `Action<AmazonLambdaConfig>?` | `null` | Optional callback to configure the `AmazonLambdaConfig` — set region, endpoint override (LocalStack), etc. |

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
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
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

You can use Lambda workers alongside HTTP remote workers and SQS workers — each for different trains:

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

## How It Works

When the JobDispatcher processes a work queue entry, it checks the `JobSubmitterRoutingConfiguration` for the entry's train name. If a route exists to `LambdaJobSubmitter`, the submitter:

1. Serializes a `RemoteJobRequest` containing the metadata ID and optional input
2. Wraps it in a `LambdaEnvelope` with `Type = Execute`
3. Calls `IAmazonLambda.InvokeAsync()` with `InvocationType.Event` (fire-and-forget)
4. Checks `response.FunctionError` — throws `TrainException` if the Lambda failed
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
| `LambdaWorkerOptions` | Singleton | Configuration options |
| `IAmazonLambda` | Singleton | AWS Lambda client |
| `LambdaJobSubmitter` | Scoped (concrete type) | Dispatches jobs via Lambda SDK — resolved per train via routing |

> **Note:** `UseLambdaWorkers()` does **not** replace the default `IJobSubmitter`. Local workers continue to run for trains not routed to this function.

## Limitations

- **Payload size limit:** Lambda invocation payloads are limited to 256 KB. If your serialized train input exceeds this, store the data externally and pass a reference.
- **No HTTP retries:** Unlike `UseRemoteWorkers()`, there is no retry configuration — Lambda handles retries at the infrastructure level for async (`Event`) invocations.
- **Cancellation is process-local:** Same limitation as other remote execution models — dashboard "Cancel" only affects trains on the same process.

## See Also

- [Remote Execution](/docs/scheduler/remote-execution) — architecture overview and deployment models
- [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run) — offload synchronous runs to Lambda
- [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function) — the Lambda receiver base class
- [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) — HTTP-based per-train remote dispatch (alternative transport)
- [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers) — SQS-based per-train dispatch (alternative transport)
