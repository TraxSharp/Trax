using System.Text.Json;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;
using Trax.Scheduler.Lambda.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Lambda;

namespace Trax.Scheduler.Lambda.Services;

/// <summary>
/// AWS Lambda implementation of <see cref="IJobSubmitter"/> that dispatches jobs via direct SDK invocation.
/// </summary>
/// <remarks>
/// Used by <c>UseLambdaWorkers()</c>. Wraps a <see cref="RemoteJobRequest"/> in a
/// <see cref="LambdaEnvelope"/> and invokes the Lambda function asynchronously
/// (<c>InvocationType.Event</c>). The Lambda function runs independently and writes
/// results to the shared database.
///
/// No public endpoint is created — access is governed by IAM policies.
/// </remarks>
public class LambdaJobSubmitter(
    IAmazonLambda lambdaClient,
    LambdaWorkerOptions options,
    ILogger<LambdaJobSubmitter> logger
) : IJobSubmitter
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
        await InvokeAsync(request, cancellationToken);
        return $"lambda-{Guid.NewGuid():N}";
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
        await InvokeAsync(request, cancellationToken);
        return $"lambda-{Guid.NewGuid():N}";
    }

    private async Task InvokeAsync(RemoteJobRequest request, CancellationToken cancellationToken)
    {
        var envelope = new LambdaEnvelope(
            LambdaRequestType.Execute,
            JsonSerializer.Serialize(request)
        );

        var invokeRequest = new InvokeRequest
        {
            FunctionName = options.FunctionName,
            InvocationType = InvocationType.Event,
            Payload = JsonSerializer.Serialize(envelope),
        };

        logger.LogDebug(
            "Invoking Lambda {FunctionName} (Event) for metadata {MetadataId}",
            options.FunctionName,
            request.MetadataId
        );

        var response = await lambdaClient.InvokeAsync(invokeRequest, cancellationToken);

        if (!string.IsNullOrEmpty(response.FunctionError))
        {
            throw new TrainException(
                $"Lambda function '{options.FunctionName}' returned error: {response.FunctionError}"
            );
        }
    }
}
