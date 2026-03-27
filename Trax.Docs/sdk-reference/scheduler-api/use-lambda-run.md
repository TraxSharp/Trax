---
layout: default
title: UseLambdaRun
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 10.2
---

# UseLambdaRun

Offloads synchronous `run` execution to an AWS Lambda function via direct SDK invocation instead of executing in-process. The call blocks until the Lambda completes and returns the train output. No public endpoint is created — access is governed by IAM policies.

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

`SchedulerConfigurationBuilder` — for continued fluent chaining.

## LambdaRunOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FunctionName` | `string` | _(required)_ | The Lambda function name, ARN, or partial ARN to invoke |
| `ConfigureLambdaClient` | `Action<AmazonLambdaConfig>?` | `null` | Optional callback to configure the `AmazonLambdaConfig` — set region, endpoint override (LocalStack), etc. |

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
| `IRunExecutor` -> `LambdaRunExecutor` | Scoped | Dispatches run requests via Lambda SDK, blocks until response |

> **Note:** Without `UseLambdaRun()`, the default `LocalRunExecutor` executes trains in-process via `ITrainBus.RunAsync()`. `UseLambdaRun()` overrides this.

## How It Works

When a GraphQL `run*` mutation is called, the `LambdaRunExecutor`:

1. Serializes a `RemoteRunRequest` containing the train name, input JSON, and input type
2. Wraps it in a `LambdaEnvelope` with `Type = Run`
3. Calls `IAmazonLambda.InvokeAsync()` with `InvocationType.RequestResponse`
4. Blocks until the Lambda completes
5. Reads `response.FunctionError` — throws `TrainException` if the Lambda failed at the infrastructure level
6. Deserializes the response payload as `RemoteRunResponse`
7. On success: returns the train output to GraphQL
8. On error (`RemoteRunResponse.IsError`): throws `TrainException` with the remote error details

## Differences from UseRemoteRun

| | UseLambdaRun | UseRemoteRun |
|---|---|---|
| **Transport** | AWS SDK direct invocation | HTTP POST |
| **Public endpoint** | None — IAM-governed | Required (Function URL, API Gateway, or similar) |
| **Package** | `Trax.Scheduler.Lambda` | `Trax.Scheduler` (built-in) |
| **Retry** | Handled by AWS SDK | Configurable `HttpRetryOptions` |
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
- **Cancellation is process-local:** Same limitation as other remote execution models.

## See Also

- [Remote Execution](/docs/scheduler/remote-execution) — architecture overview and deployment models
- [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers) — dispatch queued trains to Lambda
- [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function) — the Lambda receiver base class
- [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run) — HTTP-based remote run execution (alternative transport)
