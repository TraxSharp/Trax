using System.Text;
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
using Trax.Scheduler.Services.RequestSigning;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Lambda.Services;

/// <summary>
/// AWS Lambda implementation of <see cref="IRunExecutor"/> that dispatches synchronous run requests
/// via direct SDK invocation and blocks until the train completes. Registered by
/// <c>UseLambdaRun()</c>; not intended to be constructed directly.
/// </summary>
/// <remarks>
/// Used by <c>UseLambdaRun()</c>. Wraps a <see cref="RemoteRunRequest"/> in a
/// <see cref="LambdaEnvelope"/> and invokes the Lambda function synchronously
/// (<c>InvocationType.RequestResponse</c>). The response payload contains a
/// <see cref="RemoteRunResponse"/> with the serialized train output.
///
/// No public endpoint is created — access is governed by IAM policies.
/// </remarks>
internal class LambdaRunExecutor(
    IAmazonLambda lambdaClient,
    LambdaRunOptions options,
    ILogger<LambdaRunExecutor> logger
) : IRunExecutor
{
    /// <summary>
    /// Serializes <paramref name="input"/>, wraps it in a Run <see cref="LambdaEnvelope"/> (signed
    /// when <see cref="LambdaRunOptions.SigningKey"/> is set), invokes the function synchronously
    /// with retries for transient AWS failures, and waits for the train to finish. The output is
    /// read into <paramref name="outputType"/>, or, when that is an interface or abstract type,
    /// into the loaded implementation of it that the response names.
    /// </summary>
    /// <param name="trainName">The canonical name of the train the function runs.</param>
    /// <param name="input">The train input; serialized using its runtime type.</param>
    /// <param name="outputType">The output type the caller expects.</param>
    /// <param name="ct">Cancels the invocation, including waits between retries.</param>
    /// <returns>The remote run's metadata id, external id (empty when none) and output.</returns>
    /// <exception cref="RemoteRunException">
    /// The function reported a <c>FunctionError</c> (an unhandled exception or a timeout in the
    /// function), or returned an empty payload.
    /// </exception>
    /// <exception cref="TrainException">
    /// The train failed inside the function (rebuilt by
    /// <see cref="RemoteRunResponse.ToTrainException"/>), or the output type the response names is
    /// not a loaded implementation of <paramref name="outputType"/>.
    /// </exception>
    /// <exception cref="Amazon.Runtime.AmazonServiceException">
    /// The invocation itself failed with a non-transient AWS error, or with a transient one that
    /// outlasted <see cref="LambdaRunOptions.Retry"/>. A network failure that outlasts the retries
    /// surfaces as <see cref="HttpRequestException"/>.
    /// </exception>
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
        var payloadJson = JsonSerializer.Serialize(runRequest);
        var envelope = new LambdaEnvelope(LambdaRequestType.Run, payloadJson)
        {
            Signature = options.SigningKey is { } key
                ? RunnerRequestSignature.Create(
                    key,
                    RunnerRequestPurpose.Run,
                    Encoding.UTF8.GetBytes(payloadJson)
                )
                : null,
        };

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

        var invokeResponse = await LambdaRetryHelper.InvokeWithRetryAsync(
            lambdaClient,
            invokeRequest,
            options.Retry,
            logger,
            ct
        );

        if (!string.IsNullOrEmpty(invokeResponse.FunctionError))
        {
            var errorPayload = await ReadPayloadAsync(invokeResponse);
            throw new RemoteRunException(
                $"Lambda function '{options.FunctionName}' returned error: "
                    + $"{invokeResponse.FunctionError}. {errorPayload}"
            );
        }

        var response =
            await JsonSerializer.DeserializeAsync<RemoteRunResponse>(
                invokeResponse.Payload,
                RemoteRunJson.Read,
                cancellationToken: ct
            ) ?? throw new RemoteRunException("Lambda function returned null response.");

        // Shared with the HTTP executor, so a field the worker sends back, such as its failure
        // classification, is carried by both transports rather than by whichever was updated.
        if (response.IsError)
            throw response.ToTrainException();

        // Read into the output type the caller expects, or, when that is an interface or abstract
        // type, into the loaded implementation of it the response names; never a type loaded by name.
        var output = RemoteRunOutput.Read(response.OutputJson, outputType, response.OutputType);

        return new RunTrainResult(response.MetadataId, response.ExternalId ?? "", output);
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
