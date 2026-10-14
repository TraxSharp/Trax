---
title: UseSqsWorkers
description: Reference for UseSqsWorkers and SqsJobRunnerHandler, which route chosen trains' queued runs to an Amazon SQS queue and run them in a Lambda consumer.
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 13
package: Trax.Scheduler.Sqs
namespace:
  - Trax.Scheduler.Sqs.Extensions
  - Trax.Scheduler.Sqs.Configuration
  - Trax.Scheduler.Sqs.Lambda
source:
  - Trax.Scheduler/src/Trax.Scheduler.Sqs/Extensions/SqsSchedulerExtensions.cs
  - Trax.Scheduler/src/Trax.Scheduler.Sqs/Configuration/SqsWorkerOptions.cs
  - Trax.Scheduler/src/Trax.Scheduler.Sqs/Lambda/SqsJobRunnerHandler.cs
  - Trax.Scheduler/src/Trax.Scheduler.Sqs/Services/SqsJobSubmitter.cs
---

# UseSqsWorkers

Routes chosen trains' queued runs to an Amazon SQS queue. A consumer, normally an AWS Lambda
function using `SqsJobRunnerHandler`, takes the messages and runs the jobs. Trains not routed here
keep running on the local workers.

## Signatures

```csharp fragment
public static SchedulerConfigurationBuilder UseSqsWorkers(
    this SchedulerConfigurationBuilder builder,
    Action<SqsWorkerOptions> configure,
    Action<SubmitterRouting>? routing = null
)

public class SqsJobRunnerHandler
{
    public SqsJobRunnerHandler(IServiceProvider serviceProvider);
    public Task<SQSBatchResponse> HandleBatchAsync(SQSEvent sqsEvent, CancellationToken cancellationToken = default);
    public Task HandleAsync(SQSEvent sqsEvent, CancellationToken cancellationToken = default);
}
```

## Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `configure` | `Action<SqsWorkerOptions>` | Sets the queue URL and client options |
| `routing` | `Action<SubmitterRouting>?` | Names the trains sent to this queue with `ForTrain<TTrain>()`, which returns the routing for chaining. When omitted, no train is routed here by name; `[TraxRemote]` trains are, if this is the first `UseRemoteWorkers`, `UseSqsWorkers` or `UseLambdaWorkers` call. With none of the three, a `[TraxRemote]` train fails the scheduler's build |

### SqsWorkerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `QueueUrl` | `string` | required | The queue URL, for example `https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs` |
| `ConfigureSqsClient` | `Action<AmazonSQSConfig>?` | `null` | Configures the SQS client: region, a LocalStack `ServiceURL`, and so on |
| `MessageGroupId` | `string?` | `null` | For a FIFO queue, a fixed message group id. Null gives each message its own group (no ordering). Ignored for a standard queue |
| `SigningKey` | `byte[]?` | `null` | The key shared with the consumer's `AddTraxJobRunner(runner => runner.SigningKey = ...)`, at least 32 bytes. When set, each message carries a `Trax-Signature` message attribute over its body. See [Authorization Posture](/docs/scheduler/remote-execution#authorization-posture) |

### SqsJobRunnerHandler

| Member | Returns | On a record that must be delivered again |
|--------|---------|-------------------------------------------|
| `SqsJobRunnerHandler(IServiceProvider)` | | Takes the consumer's built service provider, which has `AddTraxJobRunner` registered |
| `HandleBatchAsync(SQSEvent, CancellationToken)` | `SQSBatchResponse` | Reports that record only, as a batch item failure |
| `HandleAsync(SQSEvent, CancellationToken)` | `Task` | Runs every record, then throws the first such record's exception, so Lambda retries the whole batch |

## Returns

`UseSqsWorkers` returns the `SchedulerConfigurationBuilder`, for chaining.

## Behavior

### Sending

A routed entry is serialized as a `RemoteJobRequest` (the metadata id and the input, if any) and
sent as one message to `QueueUrl`; on a FIFO queue with `MessageGroupId` and a
`MessageDeduplicationId`. The job id is `"sqs-{messageId}"`. A message is at most 256 KB, so a send
with a larger input fails. SQS returns nothing to the sender, so a run that needs its output uses
[UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run) or
[UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run) alongside.

A train can be routed to one submitter only; routing it to two throws `InvalidOperationException`
when the scheduler is built. `UseSqsWorkers()` may be called once per queue, each call with its own
options and client, sending only the trains it routes.

### Consuming

The handler:

1. Refuses the batch when the runner options have no posture (a `SigningKey`, or
   `AllowUnsignedRequests()` for a queue only the scheduler can write to). A message's age and
   repeats are not checked, because SQS redelivers by design; the job's `Pending` metadata row
   stops a second run.
2. Runs every record, each in its own DI scope: verifies its `Trax-Signature` attribute, reads it
   as a `RemoteJobRequest` (refusing a body that repeats a property), and calls
   `ITraxRequestHandler.ExecuteJobAsync`.
3. Reports as failures only the records that did not reach their train: a refused signature or
   body, or a failure before the run started (its row still `Pending`). SQS redelivers those, and
   the queue's dead-letter policy applies.
4. Acknowledges every other record: a train that ran, whether it succeeded or failed (a failure is
   recorded on the run's row and the manifest's retries act on it), and a redelivery of a run
   another delivery already started or finished.

Lambda reads the `SQSBatchResponse` only when the event source mapping's `FunctionResponseTypes`
includes `ReportBatchItemFailures`; without it the whole batch counts as succeeded.

A cancel reaches the consumer through the database: it sets the run's cancel flag, which the
train sees at its next junction boundary when the consumer registers `CancellationCheckProvider`
(through `AddJunctionProgress()`).

### Registered services

| Service | Lifetime | Description |
|---------|----------|-------------|
| `IAmazonSQS` (keyed) | Singleton | The call's own client, configured by its `ConfigureSqsClient` |
| SQS job submitter | Created per dispatch | An internal `IJobSubmitter` the JobDispatcher creates for each train routed to this call |
| `SqsWorkerOptions`, `IAmazonSQS` | Singleton | The first call's options and client, also registered by type |

The default `IJobSubmitter` is not replaced: local workers keep running the trains not routed here.

## Examples

```csharp compile
using Trax.Scheduler.Sqs.Extensions;

services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(assembly)
    .AddScheduler(scheduler => scheduler
        .UseSqsWorkers(
            sqs =>
            {
                sqs.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs.fifo";
                sqs.MessageGroupId = "trax-jobs";
            },
            routing => routing.ForTrain<IBatchProcessTrain>())
        .Schedule<ISyncTrain>("sync", new SyncInput(), Every.Minutes(5))));
```

A Lambda consumer:

```csharp compile
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Trax.Scheduler.Sqs.Lambda;

public class Function
{
    private static readonly IServiceProvider Services = BuildServiceProvider();
    private readonly SqsJobRunnerHandler _handler = new(Services);

    public async Task<SQSBatchResponse> FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        // Stop a few seconds before the function's timeout, so runs can record their outcome.
        using var timeout = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(5));
        return await _handler.HandleBatchAsync(sqsEvent, timeout.Token);
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddTrax(trax => trax
            .AddEffects(effects => effects.UsePostgres(Environment.GetEnvironmentVariable("TRAX_DB")!))
            .AddMediator(typeof(Function).Assembly));
        var signingKey = Convert.FromBase64String(
            Environment.GetEnvironmentVariable("TRAX_RUNNER_SIGNING_KEY")!);
        services.AddTraxJobRunner(runner => runner.SigningKey = signingKey);
        return services.BuildServiceProvider();
    }
}
```

## See also

- [Run trains on SQS workers](/docs/scheduler/run-trains-on-sqs-workers), setting up the queue, the consumer and the scheduler
- [How remote dispatch works](/docs/scheduler/how-remote-dispatch-works)
- [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner), the consumer's runner
- [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) and [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers), the other transports
