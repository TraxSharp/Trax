using System.Text;
using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RequestSigning;

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
/// The host must register <c>AddTrax()</c>, <c>AddMediator()</c>, and
/// <c>AddTraxJobRunner(runner => ...)</c> before building the <see cref="IServiceProvider"/>. The
/// runner options need a <c>SigningKey</c> matching the scheduler's <c>SqsWorkerOptions.SigningKey</c>,
/// or <c>AllowUnsignedRequests()</c> for a queue that only the scheduler can write to; without
/// either, every batch is refused. A signed message is checked for its signature only, not its age
/// or a repeat, because SQS redelivers the same message by design; the job's Pending metadata row
/// is what stops it running twice.
/// </remarks>
public class SqsJobRunnerHandler(IServiceProvider serviceProvider)
{
    /// <summary>
    /// How a message body is read: System.Text.Json's defaults, with a repeated property refused
    /// rather than resolved by whichever copy comes last.
    /// </summary>
    private static readonly JsonSerializerOptions EnvelopeOptions = new()
    {
        AllowDuplicateProperties = false,
    };

    /// <summary>
    /// Processes all SQS records in the event, running each through <see cref="ITraxRequestHandler"/>.
    /// </summary>
    /// <param name="sqsEvent">The SQS event containing one or more records</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task HandleAsync(SQSEvent sqsEvent, CancellationToken cancellationToken = default)
    {
        var verifier =
            serviceProvider.GetService<RunnerRequestVerifier>()
            ?? throw new InvalidOperationException(
                "SqsJobRunnerHandler requires AddTraxJobRunner(runner => ...) with a SigningKey "
                    + "or AllowUnsignedRequests()."
            );
        verifier.EnsurePosture(nameof(SqsJobRunnerHandler));

        foreach (var record in sqsEvent.Records)
        {
            using var scope = serviceProvider.CreateScope();
            var sp = scope.ServiceProvider;
            var logger = sp.GetRequiredService<ILogger<SqsJobRunnerHandler>>();

            try
            {
                var body = Encoding.UTF8.GetBytes(record.Body ?? "");
                var signature =
                    record.MessageAttributes is { } attributes
                    && attributes.TryGetValue(RunnerRequestSignature.HeaderName, out var attribute)
                        ? attribute.StringValue
                        : null;

                var verdict = await verifier.VerifyAsync(
                    RunnerRequestPurpose.Execute,
                    body,
                    signature,
                    requireFresh: false
                );
                if (verdict != RunnerRequestVerdict.Accepted)
                    throw new InvalidOperationException(
                        $"Refused SQS message {record.MessageId}: signature {verdict}."
                    );

                var request =
                    JsonSerializer.Deserialize<RemoteJobRequest>(body, EnvelopeOptions)
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
