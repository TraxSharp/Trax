using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Trax.Effect.Utils;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Sqs.Configuration;

namespace Trax.Scheduler.Sqs.Services;

/// <summary>
/// SQS implementation of <see cref="IJobSubmitter"/> that dispatches jobs to an SQS queue.
/// </summary>
/// <remarks>
/// Used by <c>UseSqsWorkers()</c>. Serializes a <see cref="RemoteJobRequest"/> as JSON
/// and sends it as an SQS message to the configured <see cref="SqsWorkerOptions.QueueUrl"/>.
/// A Lambda function (or other SQS consumer) runs <see cref="Trains.JobRunner.JobRunnerTrain"/>
/// to execute the train.
/// </remarks>
public class SqsJobSubmitter(IAmazonSQS sqsClient, SqsWorkerOptions options) : IJobSubmitter
{
    /// <inheritdoc />
    public Task<string> EnqueueAsync(long metadataId) =>
        EnqueueAsync(metadataId, CancellationToken.None);

    /// <inheritdoc />
    public Task<string> EnqueueAsync(long metadataId, object input) =>
        EnqueueAsync(metadataId, input, CancellationToken.None);

    /// <inheritdoc />
    public async Task<string> EnqueueAsync(long metadataId, CancellationToken cancellationToken)
    {
        var request = new RemoteJobRequest(metadataId);
        return await SendMessageAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> EnqueueAsync(
        long metadataId,
        object input,
        CancellationToken cancellationToken
    )
    {
        var inputJson = JsonSerializer.Serialize(
            input,
            input.GetType(),
            TraxJsonSerializationOptions.ManifestProperties
        );

        var request = new RemoteJobRequest(metadataId, inputJson, input.GetType().FullName);
        return await SendMessageAsync(request, cancellationToken);
    }

    private async Task<string> SendMessageAsync(
        RemoteJobRequest request,
        CancellationToken cancellationToken
    )
    {
        var body = JsonSerializer.Serialize(request);

        var sendRequest = new SendMessageRequest
        {
            QueueUrl = options.QueueUrl,
            MessageBody = body,
        };

        var isFifo = options.QueueUrl.EndsWith(".fifo", StringComparison.OrdinalIgnoreCase);

        if (isFifo)
        {
            sendRequest.MessageGroupId = options.MessageGroupId ?? Guid.NewGuid().ToString("N");
            sendRequest.MessageDeduplicationId = Guid.NewGuid().ToString("N");
        }

        var response = await sqsClient.SendMessageAsync(sendRequest, cancellationToken);
        return $"sqs-{response.MessageId}";
    }
}
