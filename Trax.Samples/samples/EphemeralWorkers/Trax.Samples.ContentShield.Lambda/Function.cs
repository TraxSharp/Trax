// ─────────────────────────────────────────────────────────────────────────────
// ContentShield — AWS Lambda Function (SQS-triggered execution)
//
// An AWS Lambda function that receives SQS messages from the Trax scheduler
// and executes trains. This is the SQS equivalent of the HTTP Runner
// (ContentShield.Runner) — same trains, different transport.
//
// How it works:
//   1. The API dispatches jobs via UseSqsWorkers() (sends RemoteJobRequest to SQS)
//   2. SQS triggers this Lambda function for each message
//   3. SqsJobRunnerHandler deserializes the input, runs JobRunnerTrain, and returns
//   4. SQS automatically retries on failure and dead-letters after max retries
//
// API configuration (sends to SQS instead of HTTP):
//   scheduler.UseSqsWorkers(sqs =>
//       sqs.QueueUrl = Environment.GetEnvironmentVariable("TRAX_QUEUE_URL")!
//   );
//
// Environment variables:
//   TRAX_CONNECTION_STRING  — Postgres connection string
//   TRAX_RABBITMQ_URL      — RabbitMQ connection string (optional, for subscriptions)
// ─────────────────────────────────────────────────────────────────────────────

using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Lambda.SQSEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StepProvider.Progress.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Sqs.Lambda;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace Trax.Samples.ContentShield.Lambda;

public class Function
{
    private static readonly IServiceProvider Services = BuildServiceProvider();
    private readonly SqsJobRunnerHandler _handler = new(Services);

    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        using var cts = new CancellationTokenSource(context.RemainingTime);
        await _handler.HandleAsync(sqsEvent, cts.Token);
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("TRAX_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "TRAX_CONNECTION_STRING environment variable is not set."
            );

        var services = new ServiceCollection();

        services.AddLogging(logging =>
            logging.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information)
        );

        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                    effects
                        .UsePostgres(connectionString)
                        .AddJson()
                        .SaveTrainParameters()
                        .AddStepProgress()
                )
                .AddMediator(typeof(ReviewContentTrain).Assembly)
        );

        services.AddTraxJobRunner();

        return services.BuildServiceProvider();
    }
}
