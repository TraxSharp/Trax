---
layout: default
title: UseSqsWorkers
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 10
---

# UseSqsWorkers

Routes specific trains to an Amazon SQS queue for execution. Trains not included in the routing configuration continue to execute locally. Messages are consumed by an AWS Lambda function (or any SQS consumer) that runs `JobRunnerTrain`.

## Package

```
dotnet add package Trax.Scheduler.Sqs
```

## Signature

```csharp
public static SchedulerConfigurationBuilder UseSqsWorkers(
    this SchedulerConfigurationBuilder builder,
    Action<SqsWorkerOptions> configure,
    Action<SubmitterRouting>? routing = null
)
```

Defined in `Trax.Scheduler.Sqs.Extensions.SqsSchedulerExtensions`.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configure` | `Action<SqsWorkerOptions>` | Yes | Callback to set the SQS queue URL and client options |
| `routing` | `Action<SubmitterRouting>?` | No | Callback to specify which trains should be dispatched to this SQS queue. When omitted, no train is routed here explicitly; `[TraxRemote]`-attributed trains are, if this is the first `UseRemoteWorkers`, `UseSqsWorkers` or `UseLambdaWorkers` call. With none of the three, a `[TraxRemote]` train fails the scheduler's build. |

## Returns

`SchedulerConfigurationBuilder`, for continued fluent chaining.

## SqsWorkerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `QueueUrl` | `string` | _(required)_ | The SQS queue URL (e.g., `https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs`) |
| `ConfigureSqsClient` | `Action<AmazonSQSConfig>?` | `null` | Optional callback to configure the SQS client (set region, endpoint override for LocalStack, etc.) |
| `MessageGroupId` | `string?` | `null` | For FIFO queues: a fixed message group ID. When null, each message gets a unique group ID (no ordering). Ignored for standard queues. |
| `SigningKey` | `byte[]?` | `null` | The key shared with the consumer's `AddTraxJobRunner(runner => runner.SigningKey = ...)`, at least 32 bytes. When set, each message carries a `Trax-Signature` message attribute over its body. See [Authorization Posture](/docs/scheduler/remote-execution#authorization-posture) |

## SubmitterRouting

| Method | Description |
|--------|-------------|
| `ForTrain<TTrain>()` | Routes the specified train type to this SQS queue. Returns the routing instance for chaining. |

## Examples

### Basic Usage

```csharp
using Trax.Scheduler.Sqs.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
    )
    .AddMediator(assemblies)
    .AddScheduler(scheduler => scheduler
        .UseSqsWorkers(
            sqs => sqs.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs",
            routing => routing.ForTrain<IBatchProcessTrain>())
        .Schedule<IMyTrain>("my-job", new MyInput(), Every.Minutes(5))
        .Schedule<IBatchProcessTrain>("batch", new BatchInput(), Every.Hours(1))
    )
);
```

In this example, `IBatchProcessTrain` is dispatched to SQS. `IMyTrain` executes locally.

### With Custom Region

```csharp
.UseSqsWorkers(
    sqs =>
    {
        sqs.QueueUrl = "https://sqs.eu-west-1.amazonaws.com/123456789/trax-jobs";
        sqs.ConfigureSqsClient = config =>
            config.RegionEndpoint = Amazon.RegionEndpoint.EUWest1;
    },
    routing => routing.ForTrain<IBatchProcessTrain>())
```

### With LocalStack (Development)

```csharp
.UseSqsWorkers(
    sqs =>
    {
        sqs.QueueUrl = "http://localhost:4566/000000000000/trax-jobs";
        sqs.ConfigureSqsClient = config =>
        {
            config.ServiceURL = "http://localhost:4566";
            config.AuthenticationRegion = "us-east-1";
        };
    },
    routing => routing.ForTrain<IBatchProcessTrain>())
```

### FIFO Queue with Ordering

```csharp
.UseSqsWorkers(
    sqs =>
    {
        sqs.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs.fifo";
        sqs.MessageGroupId = "trax-jobs";
    },
    routing => routing.ForTrain<IOrderedTrain>())
```

### Mixed with Remote Workers

You can use both SQS and HTTP remote workers, each for different trains:

```csharp
.AddScheduler(scheduler => scheduler
    .UseRemoteWorkers(
        remote => remote.BaseUrl = "https://gpu-workers/trax/execute",
        routing => routing.ForTrain<IAiInferenceTrain>())
    .UseSqsWorkers(
        sqs => sqs.QueueUrl = "https://sqs.../trax-jobs",
        routing => routing.ForTrain<IBatchProcessTrain>())
)
```

Each train can only be routed to one submitter. Routing the same train to both throws `InvalidOperationException` at build time.

`UseSqsWorkers()` can also be called more than once, one call per queue. Each call keeps its own `SqsWorkerOptions` and its own SQS client, and sends only the trains it routes.

## Lambda Consumer

On the consumer side, use `SqsJobRunnerHandler` in an AWS Lambda function:

```csharp
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Trax.Scheduler.Sqs.Lambda;

public class Function
{
    private static readonly IServiceProvider Services = BuildServiceProvider();
    private readonly SqsJobRunnerHandler _handler = new(Services);

    public Task<SQSBatchResponse> FunctionHandler(SQSEvent sqsEvent, ILambdaContext context) =>
        _handler.HandleBatchAsync(sqsEvent, context.CancellationToken);

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddTrax(trax => trax
            .AddEffects(effects => effects.UsePostgres(connectionString))
            .AddMediator(typeof(MyTrain).Assembly)
        );
        var signingKey = Convert.FromBase64String(
            Environment.GetEnvironmentVariable("TRAX_RUNNER_SIGNING_KEY")!
        );
        services.AddTraxJobRunner(runner => runner.SigningKey = signingKey);
        return services.BuildServiceProvider();
    }
}
```

Enable `ReportBatchItemFailures` in the event source mapping's `FunctionResponseTypes`; without it Lambda ignores the returned `SQSBatchResponse` and treats the whole batch as succeeded.

The handler:
1. Refuses the batch when the runner options have no posture (a `SigningKey`, or `AllowUnsignedRequests()` for a queue only the scheduler can write to). The message's age and repeats are not checked, because SQS redelivers by design; the job's `Pending` metadata row stops a second run
2. Runs every record in the batch, each in its own DI scope: verifies its `Trax-Signature` attribute, deserializes it as a `RemoteJobRequest` (refusing a body that repeats a property), and delegates to `ITraxRequestHandler.ExecuteJobAsync`
3. Returns, as batch item failures, only the records that did not reach their train: a refused signature or body, or a failure before the run was started (the run's row is still `Pending`). SQS redelivers those, and its dead-letter queue policy applies
4. Acknowledges every other record: a train that ran, whether it succeeded or failed (a failure is recorded on the run's row and the manifest's retries act on it), and a redelivery of a run another delivery already started or finished

| Method | Returns | On a record that must be delivered again |
|--------|---------|-------------------------------------------|
| `HandleBatchAsync(SQSEvent, CancellationToken)` | `SQSBatchResponse` | Reports that record only |
| `HandleAsync(SQSEvent, CancellationToken)` | `Task` | Runs every record, then throws the first such record's exception, so Lambda retries the whole batch |

## Registered Services

`UseSqsWorkers()` registers:

| Service | Lifetime | Description |
|---------|----------|-------------|
| `IAmazonSQS` (keyed) | Singleton | The call's own SQS client, configured by its `ConfigureSqsClient` |
| SQS job submitter | Created per dispatch | An internal `IJobSubmitter` that sends jobs to the call's queue with its own options and client. The JobDispatcher creates it for each train routed to this call; application code does not resolve it |
| `SqsWorkerOptions`, `IAmazonSQS` | Singleton | The **first** call's options and client, also registered by type, as before |

> **Note:** `UseSqsWorkers()` does **not** replace the default `IJobSubmitter`. Local workers continue to run for trains not routed to this queue.

## How It Works

When the JobDispatcher processes a work queue entry, it checks whether the entry's train is routed to this queue. If it is, the SQS submitter:

1. Serializes a `RemoteJobRequest` containing the metadata ID and optional input
2. Sends the JSON as an SQS message to `QueueUrl`
3. For FIFO queues, sets `MessageGroupId` and `MessageDeduplicationId`
4. Returns a job ID in the format `"sqs-{messageId}"`

## SQS Queue Configuration

### Standard Queue (Recommended)

Standard queues provide nearly unlimited throughput with at-least-once delivery. This is the best fit for most Trax workloads.

### FIFO Queue

FIFO queues guarantee ordering within a message group (300 messages/sec, or 3,000 with batching). Use this only when job execution order matters.

### Dead Letter Queue

Configure a DLQ on your SQS queue for jobs that fail repeatedly:

```json
{
  "RedrivePolicy": {
    "deadLetterTargetArn": "arn:aws:sqs:us-east-1:123456789:trax-jobs-dlq",
    "maxReceiveCount": 3
  }
}
```

### IAM Permissions

The API process needs `sqs:SendMessage`. The Lambda function needs `sqs:ReceiveMessage`, `sqs:DeleteMessage`, and `sqs:GetQueueAttributes`.

## Limitations

- **Message size limit:** SQS messages are limited to 256 KB. If your serialized train input exceeds this, the send will fail. For large inputs, store the data externally and pass a reference.
- **No synchronous return:** SQS is fire-and-forget. For mutations that need a return value, continue using [`UseRemoteRun()`](/docs/sdk-reference/scheduler-api/use-remote-run) alongside `UseSqsWorkers()`.
- **Cancellation reaches the function through the database:** Dashboard "Cancel" sets the run's cancel flag, which the train sees at its next junction boundary when the function registers `CancellationCheckProvider` (via `AddJunctionProgress()`). A junction already running is not interrupted. See [Remote Execution](/docs/scheduler/remote-execution#limitations).

## See Also

- [Remote Execution](/docs/scheduler/remote-execution): architecture overview and deployment models
- [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers): HTTP-based per-train remote dispatch (alternative transport)
- [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers): Lambda-based per-train dispatch (direct SDK invocation, alternative transport)
- [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner): setting up the remote receiver
- [ConfigureLocalWorkers](/docs/sdk-reference/scheduler-api/use-local-workers): customizing the local (default) execution backend
