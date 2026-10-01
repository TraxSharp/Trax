using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RequestSigning;

namespace Trax.Scheduler.Sqs.Lambda;

/// <summary>
/// AWS Lambda handler that processes SQS messages containing <see cref="RemoteJobRequest"/> payloads.
/// </summary>
/// <remarks>
/// Each SQS record is deserialized as a <see cref="RemoteJobRequest"/> and executed via
/// <see cref="ITraxRequestHandler"/>. Every record in a batch is processed, and
/// <see cref="HandleBatchAsync"/> reports back only the records that should be delivered again,
/// so SQS returns those to the queue and deletes the rest. That requires the event source
/// mapping to have <c>ReportBatchItemFailures</c> in its <c>FunctionResponseTypes</c>; without it
/// Lambda ignores the response and treats the whole batch as succeeded.
///
/// A record is acknowledged (not reported) when:
/// <list type="bullet">
///   <item>its train ran, whether it succeeded or failed: the outcome is recorded on the run's
///   row, and the scheduler's retries and dead letters act on it, so a redelivery would not run it
///   again anyway;</item>
///   <item>another delivery of the same job already started or finished it (SQS delivers at least
///   once): the JobRunner completes without running it.</item>
/// </list>
/// A record is reported when it could not be delivered to the train: its signature or body is
/// refused, or the runner failed before the run was started (the row is still <c>Pending</c>, or
/// its state could not be read). SQS redelivers it until the queue's <c>maxReceiveCount</c> sends
/// it to the dead-letter queue.
///
/// Usage in a Lambda function:
/// <code>
/// public class Function
/// {
///     private static readonly IServiceProvider Services = BuildServiceProvider();
///     private readonly SqsJobRunnerHandler _handler = new(Services);
///
///     public Task&lt;SQSBatchResponse&gt; FunctionHandler(SQSEvent sqsEvent, ILambdaContext context) =>
///         _handler.HandleBatchAsync(sqsEvent, context.CancellationToken);
/// }
/// </code>
///
/// The host must register <c>AddTrax()</c> with a data provider, <c>AddMediator()</c>, and
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
    /// Processes every SQS record in the event and returns the ones SQS should deliver again.
    /// </summary>
    /// <param name="sqsEvent">The SQS event containing one or more records</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// The batch item failures: the records that could not be delivered to their train. Return it
    /// from the Lambda handler, with <c>ReportBatchItemFailures</c> enabled on the event source
    /// mapping.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The runner has no authorization posture, so no record can be processed.
    /// </exception>
    public async Task<SQSBatchResponse> HandleBatchAsync(
        SQSEvent sqsEvent,
        CancellationToken cancellationToken = default
    )
    {
        var failures = await ProcessAsync(sqsEvent, cancellationToken);

        return new SQSBatchResponse(
            failures
                .Select(f => new SQSBatchResponse.BatchItemFailure { ItemIdentifier = f.MessageId })
                .ToList()
        );
    }

    /// <summary>
    /// Processes every SQS record in the event, and throws if any of them should be delivered
    /// again, so that Lambda retries the whole batch.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="HandleBatchAsync"/>, which lets SQS redeliver only the records that
    /// failed. Here a batch with one undeliverable record is redelivered whole; the records in it
    /// that already ran are acknowledged on redelivery without running again.
    /// </remarks>
    /// <param name="sqsEvent">The SQS event containing one or more records</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task HandleAsync(SQSEvent sqsEvent, CancellationToken cancellationToken = default)
    {
        var failures = await ProcessAsync(sqsEvent, cancellationToken);

        if (failures.Count > 0)
            ExceptionDispatchInfo.Capture(failures[0].Exception).Throw();
    }

    private async Task<List<RecordFailure>> ProcessAsync(
        SQSEvent sqsEvent,
        CancellationToken cancellationToken
    )
    {
        var verifier =
            serviceProvider.GetService<RunnerRequestVerifier>()
            ?? throw new InvalidOperationException(
                "SqsJobRunnerHandler requires AddTraxJobRunner(runner => ...) with a SigningKey "
                    + "or AllowUnsignedRequests()."
            );
        verifier.EnsurePosture(nameof(SqsJobRunnerHandler));

        var failures = new List<RecordFailure>();

        foreach (var record in sqsEvent.Records)
        {
            using var scope = serviceProvider.CreateScope();
            var sp = scope.ServiceProvider;
            var logger = sp.GetRequiredService<ILogger<SqsJobRunnerHandler>>();

            RemoteJobRequest request;
            try
            {
                request = await ReadAsync(record, verifier);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Refused SQS message {MessageId}", record.MessageId);
                failures.Add(new RecordFailure(record.MessageId, ex));
                continue;
            }

            try
            {
                var handler = sp.GetRequiredService<ITraxRequestHandler>();
                await handler.ExecuteJobAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                if (await RunWasStartedAsync(sp, request.MetadataId))
                {
                    // The train ran and its failure is recorded on the run's row. Delivering the
                    // message again would not run it again; the scheduler's retries act on it.
                    logger.LogWarning(
                        ex,
                        "SQS message {MessageId} ran Metadata {MetadataId}, which failed; its "
                            + "outcome is recorded, so the message is acknowledged",
                        record.MessageId,
                        request.MetadataId
                    );
                    continue;
                }

                logger.LogError(
                    ex,
                    "SQS job execution failed for message {MessageId} before Metadata {MetadataId} "
                        + "was started; the message will be delivered again",
                    record.MessageId,
                    request.MetadataId
                );
                failures.Add(new RecordFailure(record.MessageId, ex));
            }
        }

        return failures;
    }

    private static async Task<RemoteJobRequest> ReadAsync(
        SQSEvent.SQSMessage record,
        RunnerRequestVerifier verifier
    )
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

        return JsonSerializer.Deserialize<RemoteJobRequest>(body, EnvelopeOptions)
            ?? throw new InvalidOperationException(
                "Failed to deserialize SQS message body as RemoteJobRequest."
            );
    }

    /// <summary>
    /// Whether the run's row has left <c>Pending</c>, meaning a delivery started it and its outcome
    /// is (or will be) recorded there. False when the row is still Pending, is missing, or cannot
    /// be read, so the message is delivered again rather than lost.
    /// </summary>
    private static async Task<bool> RunWasStartedAsync(IServiceProvider services, long metadataId)
    {
        if (services.GetService<IDataContextProviderFactory>() is not { } factory)
            return false;

        try
        {
            await using var context = (IDataContext)
                await factory.CreateDbContextAsync(CancellationToken.None);
            var state = await context
                .Metadatas.Where(m => m.Id == metadataId)
                .Select(m => (TrainState?)m.TrainState)
                .FirstOrDefaultAsync(CancellationToken.None);

            return state is { } started && started != TrainState.Pending;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed record RecordFailure(string MessageId, Exception Exception);
}
