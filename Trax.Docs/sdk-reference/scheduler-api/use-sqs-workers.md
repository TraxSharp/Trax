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
    Action<SubmitterRouting> routing
)
```

Defined in `Trax.Scheduler.Sqs.Extensions.SqsSchedulerExtensions`.

## Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `configure` | `Action<SqsWorkerOptions>` | Yes | Callback to set the SQS queue URL and client options |
| `routing` | `Action<SubmitterRouting>` | Yes | Callback to specify which trains should be dispatched to this SQS queue |

## Returns

`SchedulerConfigurationBuilder` — for continued fluent chaining.

## SqsWorkerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `QueueUrl` | `string` | _(required)_ | The SQS queue URL (e.g., `https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs`) |
| `ConfigureSqsClient` | `Action<AmazonSQSConfig>?` | `null` | Optional callback to configure the SQS client — set region, endpoint override (LocalStack), etc. |
| `MessageGroupId` | `string?` | `null` | For FIFO queues: a fixed message group ID. When null, each message gets a unique group ID (no ordering). Ignored for standard queues. |

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
        .Schedule<IMyTrain, MyInput>("my-job", new MyInput(), Every.Minutes(5))
        .Schedule<IBatchProcessTrain, BatchInput>("batch", new BatchInput(), Every.Hours(1))
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

You can use both SQS and HTTP remote workers — each for different trains:

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

    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        await _handler.HandleAsync(sqsEvent, context.CancellationToken);
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddTrax(trax => trax
            .AddEffects(effects => effects.UsePostgres(connectionString))
            .AddMediator(typeof(MyTrain).Assembly)
        );
        services.AddTraxJobRunner();
        return services.BuildServiceProvider();
    }
}
```

The handler:
1. Deserializes each SQS record as a `RemoteJobRequest`
2. Delegates to `ITraxRequestHandler.ExecuteJobAsync` in a scoped DI container
3. Re-throws exceptions so SQS retry and dead-letter queue policies apply

## Registered Services

`UseSqsWorkers()` registers:

| Service | Lifetime | Description |
|---------|----------|-------------|
| `SqsWorkerOptions` | Singleton | Configuration options |
| `IAmazonSQS` | Singleton | AWS SQS client |
| `SqsJobSubmitter` | Scoped (concrete type) | Dispatches jobs as SQS messages — resolved per train via routing |

> **Note:** `UseSqsWorkers()` does **not** replace the default `IJobSubmitter`. Local workers continue to run for trains not routed to this queue.

## How It Works

When the JobDispatcher processes a work queue entry, it checks the `JobSubmitterRoutingConfiguration` for the entry's train name. If a route exists to `SqsJobSubmitter`, the `SqsJobSubmitter`:

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
- **No synchronous return:** SQS is fire-and-forget. For mutations that need a return value, continue using [`UseRemoteRun()`]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-remote-run.md %}) alongside `UseSqsWorkers()`.
- **Cancellation is process-local:** Same limitation as other remote execution models — dashboard "Cancel" only affects trains on the same process.

## See Also

- [Remote Execution]({{ site.baseurl }}{% link scheduler/remote-execution.md %}) — architecture overview and deployment models
- [UseRemoteWorkers]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-remote-workers.md %}) — HTTP-based per-train remote dispatch (alternative transport)
- [AddTraxJobRunner]({{ site.baseurl }}{% link sdk-reference/scheduler-api/add-trax-job-runner.md %}) — setting up the remote receiver
- [ConfigureLocalWorkers]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-local-workers.md %}) — customizing the local (default) execution backend
