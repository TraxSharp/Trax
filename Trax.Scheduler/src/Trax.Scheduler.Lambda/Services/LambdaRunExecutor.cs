using System.Text.Json;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Lambda.Configuration;
using Trax.Scheduler.Services.Lambda;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Lambda.Services;

/// <summary>
/// AWS Lambda implementation of <see cref="IRunExecutor"/> that dispatches synchronous run requests
/// via direct SDK invocation and blocks until the train completes.
/// </summary>
/// <remarks>
/// Used by <c>UseLambdaRun()</c>. Wraps a <see cref="RemoteRunRequest"/> in a
/// <see cref="LambdaEnvelope"/> and invokes the Lambda function synchronously
/// (<c>InvocationType.RequestResponse</c>). The response payload contains a
/// <see cref="RemoteRunResponse"/> with the serialized train output.
///
/// No public endpoint is created — access is governed by IAM policies.
/// </remarks>
public class LambdaRunExecutor(
    IAmazonLambda lambdaClient,
    LambdaRunOptions options,
    ILogger<LambdaRunExecutor> logger
) : IRunExecutor
{
    public async Task<RunTrainResult> ExecuteAsync(
        string trainName,
        object input,
        Type outputType,
        CancellationToken ct = default
    )
    {
        var inputJson = JsonSerializer.Serialize(
            input,
            input.GetType(),
            TraxJsonSerializationOptions.ManifestProperties
        );

        var runRequest = new RemoteRunRequest(trainName, inputJson, input.GetType().FullName!);
        var envelope = new LambdaEnvelope(
            LambdaRequestType.Run,
            JsonSerializer.Serialize(runRequest)
        );

        var invokeRequest = new InvokeRequest
        {
            FunctionName = options.FunctionName,
            InvocationType = InvocationType.RequestResponse,
            Payload = JsonSerializer.Serialize(envelope),
        };

        logger.LogDebug(
            "Invoking Lambda {FunctionName} (RequestResponse) for train {TrainName}",
            options.FunctionName,
            trainName
        );

        var invokeResponse = await lambdaClient.InvokeAsync(invokeRequest, ct);

        if (!string.IsNullOrEmpty(invokeResponse.FunctionError))
        {
            var errorPayload = await ReadPayloadAsync(invokeResponse);
            throw new TrainException(
                $"Lambda function '{options.FunctionName}' returned error: "
                    + $"{invokeResponse.FunctionError}. {errorPayload}"
            );
        }

        var response =
            await JsonSerializer.DeserializeAsync<RemoteRunResponse>(
                invokeResponse.Payload,
                cancellationToken: ct
            ) ?? throw new TrainException("Lambda function returned null response.");

        if (response.IsError)
            throw BuildExceptionFromErrorResponse(response);

        object? output = null;
        if (response.OutputJson is not null && response.OutputType is not null)
        {
            var resolvedType = TypeResolver.ResolveType(response.OutputType);
            output = JsonSerializer.Deserialize(
                response.OutputJson,
                resolvedType,
                TraxJsonSerializationOptions.ManifestProperties
            );
        }

        return new RunTrainResult(response.MetadataId, response.ExternalId ?? "", output);
    }

    private static TrainException BuildExceptionFromErrorResponse(RemoteRunResponse response)
    {
        if (response.ExceptionType is not null)
        {
            var data = new TrainExceptionData
            {
                TrainName = "",
                TrainExternalId = "",
                Type = response.ExceptionType,
                Junction = response.FailureJunction ?? "Unknown",
                Message = response.ErrorMessage ?? "Remote train execution failed",
            };

            var json = JsonSerializer.Serialize(data);
            return new TrainException(json);
        }

        return new TrainException($"Remote train execution failed: {response.ErrorMessage}");
    }

    private static async Task<string> ReadPayloadAsync(InvokeResponse response)
    {
        try
        {
            using var reader = new StreamReader(response.Payload);
            return await reader.ReadToEndAsync();
        }
        catch
        {
            return "(unable to read error payload)";
        }
    }
}
