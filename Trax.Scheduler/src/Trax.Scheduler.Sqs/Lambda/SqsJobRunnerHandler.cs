using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;

namespace Trax.Scheduler.Sqs.Lambda;

/// <summary>
/// AWS Lambda handler that processes SQS messages containing <see cref="RemoteJobRequest"/> payloads.
/// </summary>
/// <remarks>
/// Each SQS record is deserialized as a <see cref="RemoteJobRequest"/> and executed via
/// <see cref="ITraxRequestHandler"/>. Exceptions are re-thrown so that Lambda marks the message
/// as failed, allowing SQS retry and dead-letter queue policies to apply.
///
/// Usage in a Lambda function:
/// <code>
/// public class Function
/// {
///     private static readonly IServiceProvider Services = BuildServiceProvider();
///     private readonly SqsJobRunnerHandler _handler = new(Services);
///
///     public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
///     {
///         await _handler.HandleAsync(sqsEvent, context.CancellationToken);
///     }
/// }
/// </code>
///
/// The host must register <c>AddTrax()</c>, <c>AddMediator()</c>, and <c>AddTraxJobRunner()</c>
/// before building the <see cref="IServiceProvider"/>.
/// </remarks>
public class SqsJobRunnerHandler(IServiceProvider serviceProvider)
{
    /// <summary>
    /// Processes all SQS records in the event, running each through <see cref="ITraxRequestHandler"/>.
    /// </summary>
    /// <param name="sqsEvent">The SQS event containing one or more records</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task HandleAsync(SQSEvent sqsEvent, CancellationToken cancellationToken = default)
    {
        foreach (var record in sqsEvent.Records)
        {
            using var scope = serviceProvider.CreateScope();
            var sp = scope.ServiceProvider;
            var logger = sp.GetRequiredService<ILogger<SqsJobRunnerHandler>>();

            try
            {
                var request =
                    JsonSerializer.Deserialize<RemoteJobRequest>(record.Body)
                    ?? throw new InvalidOperationException(
                        "Failed to deserialize SQS message body as RemoteJobRequest."
                    );

                var handler = sp.GetRequiredService<ITraxRequestHandler>();
                await handler.ExecuteJobAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "SQS job execution failed for message {MessageId}",
                    record.MessageId
                );
                throw;
            }
        }
    }
}
