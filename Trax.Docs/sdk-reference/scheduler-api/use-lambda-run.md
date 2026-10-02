---
layout: default
title: UseLambdaRun
description: Reference for UseLambdaRun, which sends synchronous run requests to an AWS Lambda function by direct SDK invocation, with options, IAM and limitations.
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 10.2
---

# UseLambdaRun

Offloads synchronous `run` execution to an AWS Lambda function via direct SDK invocation instead of executing in-process. The call blocks until the Lambda completes and returns the train output. No public endpoint is created; access is governed by IAM policies.

## Package

```
dotnet add package Trax.Scheduler.Lambda
```

## Signature

```csharp
public static SchedulerConfigurationBuilder UseLambdaRun(
    this SchedulerConfigurationBuilder builder,
    Action<LambdaRunOptions> configure
)
```

Defined in `Trax.Scheduler.Lambda.Extensions.LambdaSchedulerExtensions`.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configure` | `Action<LambdaRunOptions>` | Yes | Callback to set the Lambda function name and client options |

## Returns

`SchedulerConfigurationBuilder`, for continued fluent chaining.

## LambdaRunOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FunctionName` | `string` | _(required)_ | The Lambda function name, ARN, or partial ARN to invoke |
| `ConfigureLambdaClient` | `Action<AmazonLambdaConfig>?` | `null` | Optional callback to configure the `AmazonLambdaConfig` (set region, endpoint override for LocalStack, etc.) |
| `SigningKey` | `byte[]?` | `null` | The key shared with the function's `ConfigureRunner`, at least 32 bytes. When set, each envelope carries a `Signature` over its `PayloadJson`. See [Authorization Posture](/docs/scheduler/remote-execution#authorization-posture) |

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
            routing => routing.ForTrain<IMyTrain>()
        )
        .UseLambdaRun(lambda =>
            lambda.FunctionName = "content-shield-runner"
        )
    )
);
```

### With Custom Region

```csharp
.UseLambdaRun(lambda =>
{
    lambda.FunctionName = "content-shield-runner";
    lambda.ConfigureLambdaClient = config =>
        config.RegionEndpoint = Amazon.RegionEndpoint.EUWest1;
})
```

### With LocalStack (Development)

```csharp
.UseLambdaRun(lambda =>
{
    lambda.FunctionName = "content-shield-runner";
    lambda.ConfigureLambdaClient = config =>
        config.ServiceURL = "http://localhost:4566";
})
```

## Registered Services

`UseLambdaRun()` registers:

| Service | Lifetime | Description |
|---------|----------|-------------|
| `LambdaRunOptions` | Singleton | Configuration options |
| `IAmazonLambda` | Singleton | AWS Lambda client |
| `IRunExecutor` | Scoped | Replaced with an internal implementation that dispatches run requests via the Lambda SDK and blocks until the response |

> **Note:** Without `UseLambdaRun()`, the default `LocalRunExecutor` executes trains in-process via `ITrainBus.RunAsync()`. `UseLambdaRun()` overrides this.

## How It Works

When a GraphQL `run*` mutation is called, the `IRunExecutor` registered by `UseLambdaRun()`:

1. Serializes a `RemoteRunRequest` containing the train name, input JSON, and input type
2. Wraps it in a `LambdaEnvelope` with `Type = Run`
3. Calls `IAmazonLambda.InvokeAsync()` with `InvocationType.RequestResponse`
4. Blocks until the Lambda completes
5. Reads `response.FunctionError` and throws `RemoteRunException` (a `TrainException`) with no public message if the Lambda failed at the infrastructure level
6. Deserializes the response payload as `RemoteRunResponse`
7. On success: returns the train output to GraphQL
8. On error (`RemoteRunResponse.IsError`): throws `RemoteRunException` with the remote error details and the runner's `PublicMessage`

## Differences from UseRemoteRun

| | UseLambdaRun | UseRemoteRun |
|---|---|---|
| **Transport** | AWS SDK direct invocation | HTTP POST |
| **Public endpoint** | None (IAM-governed) | Required (Function URL, API Gateway, or similar) |
| **Package** | `Trax.Scheduler.Lambda` | `Trax.Scheduler` (built-in) |
| **Retry** | Configurable `LambdaRetryOptions` | Configurable `HttpRetryOptions` |
| **Timeout** | Lambda execution timeout (AWS config) | `RemoteRunOptions.Timeout` (default 5 min) |

## IAM Permissions

The scheduler process needs:

```json
{
  "Effect": "Allow",
  "Action": "lambda:InvokeFunction",
  "Resource": "arn:aws:lambda:us-east-1:123456789012:function:content-shield-runner"
}
```

## Limitations

- **Payload size limit:** Lambda response payloads are limited to 6 MB (synchronous). Train outputs exceeding this will fail.
- **Execution timeout:** Lambda functions have a maximum execution time of 15 minutes. Long-running trains may time out.
- **Cancellation reaches the function through the database:** a cancel sets the run's cancel flag, which the train sees at its next junction boundary when the function registers `CancellationCheckProvider` (via `AddJunctionProgress()`). A junction already running is not interrupted. See [Remote Execution](/docs/scheduler/remote-execution#limitations).

## See Also

- [Remote Execution](/docs/scheduler/remote-execution): architecture overview and deployment models
- [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers): dispatch queued trains to Lambda
- [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function): the Lambda receiver base class
- [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run): HTTP-based remote run execution (alternative transport)
