---
title: Run trains on SQS workers
description: "Send chosen trains' queued runs to an Amazon SQS queue with UseSqsWorkers and run them in a Lambda consumer with SqsJobRunnerHandler."
type: how-to
area: scheduling
nav_order: 31
---

# Run trains on SQS workers

This sends chosen trains' queued runs to an Amazon SQS queue, and runs them in an AWS Lambda
function that consumes it. SQS returns nothing to the sender, so trains that are also run
synchronously need [UseLambdaRun or UseRemoteRun](/docs/scheduler/run-trains-on-lambda) as well.

## 1. Create the queue

A standard queue suits most workloads: it delivers at least once, with nearly unlimited
throughput. Use a FIFO queue only when the order the jobs run in matters; it is limited to 300
messages a second (3,000 with batching) per message group.

Give the queue a dead-letter queue for messages that keep failing before they reach their train:

```json
{
  "RedrivePolicy": {
    "deadLetterTargetArn": "arn:aws:sqs:us-east-1:123456789:trax-jobs-dlq",
    "maxReceiveCount": 3
  }
}
```

A message is at most 256 KB. A train whose input is larger stores it elsewhere and passes a
reference.

## 2. Write the consumer

The consumer is a Lambda function that builds a service provider with the trains, the Trax effects
and the job runner, and hands each batch to `SqsJobRunnerHandler`:

```csharp compile
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Sqs.Lambda;

public class Function
{
    private static readonly IServiceProvider Services = BuildServiceProvider();
    private readonly SqsJobRunnerHandler _handler = new(Services);

    public async Task<SQSBatchResponse> FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        using var timeout = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(5));
        return await _handler.HandleBatchAsync(sqsEvent, timeout.Token);
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax => trax
            .AddEffects(effects => effects
                .UsePostgres(Environment.GetEnvironmentVariable("TRAX_DB")!)
                .AddJunctionProgress())
            .AddMediator(typeof(Function).Assembly));

        var signingKey = Convert.FromBase64String(
            Environment.GetEnvironmentVariable("TRAX_RUNNER_SIGNING_KEY")!);
        services.AddTraxJobRunner(runner => runner.SigningKey = signingKey);
        return services.BuildServiceProvider();
    }
}
```

The signing key, at least 32 bytes, is shared with the scheduler. A queue only the scheduler can
write to may use `runner.AllowUnsignedRequests()` instead; with neither, every batch is refused.
`AddJunctionProgress()` lets a cancel from the dashboard reach a running train at its next
junction.

## 3. Connect the queue to the function

Create an event source mapping from the queue to the function, with `ReportBatchItemFailures` in
its `FunctionResponseTypes`. Without it Lambda ignores the `SQSBatchResponse` and treats every
batch as succeeded, so a record that never reached its train is not delivered again.

Give the function's role `sqs:ReceiveMessage`, `sqs:DeleteMessage` and `sqs:GetQueueAttributes` on
the queue.

## 4. Route the trains to the queue

Give the scheduler's role `sqs:SendMessage` on the queue, and route the trains with the same
signing key:

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Sqs.Extensions;

var signingKey = Convert.FromBase64String(builder.Configuration["Trax:RunnerSigningKey"]!);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(IBatchProcessTrain).Assembly)
    .AddScheduler(scheduler => scheduler
        .UseSqsWorkers(
            sqs =>
            {
                sqs.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs";
                sqs.SigningKey = signingKey;
            },
            routing => routing.ForTrain<IBatchProcessTrain>())));
```

For a FIFO queue, use its `.fifo` URL and set `sqs.MessageGroupId` to keep the jobs in one ordered
group. Trains you do not route keep running on the scheduler's local workers.

## 5. Develop against LocalStack

Point the client at LocalStack through `ConfigureSqsClient`:

```csharp fragment
sqs.QueueUrl = "http://localhost:4566/000000000000/trax-jobs";
sqs.ConfigureSqsClient = config =>
{
    config.ServiceURL = "http://localhost:4566";
    config.AuthenticationRegion = "us-east-1";
};
```

## SDK Reference

> [UseSqsWorkers](/docs/sdk-reference/scheduler-api/use-sqs-workers) | [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner) | [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run)
