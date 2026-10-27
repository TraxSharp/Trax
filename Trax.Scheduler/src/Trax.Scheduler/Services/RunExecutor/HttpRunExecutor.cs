using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Http;
using Trax.Scheduler.Services.RequestSigning;

namespace Trax.Scheduler.Services.RunExecutor;

/// <summary>
/// HTTP implementation of <see cref="IRunExecutor"/> that dispatches synchronous run requests
/// to a remote endpoint and blocks until the train completes. Registered by <c>UseRemoteRun()</c>;
/// not intended to be constructed directly.
/// </summary>
/// <remarks>
/// Used by <c>UseRemoteRun()</c>. Serializes a <see cref="RemoteRunRequest"/> as JSON
/// and POSTs it to the configured <see cref="Configuration.RemoteRunOptions.BaseUrl"/>.
/// The remote endpoint runs the train via <c>ITrainExecutionService.RunAsync()</c> and
/// returns a <see cref="RemoteRunResponse"/> with the serialized output.
///
/// Retries transient HTTP failures (429, 502, 503) with exponential backoff.
/// Configure retry behavior via <see cref="RemoteRunOptions.Retry"/>.
/// </remarks>
internal class HttpRunExecutor(
    HttpClient httpClient,
    RemoteRunOptions options,
    ILogger<HttpRunExecutor> logger
) : IRunExecutor
{
    private const int MaxErrorBodyLength = 2000;

    /// <summary>
    /// Serializes <paramref name="input"/>, POSTs it as a <see cref="RemoteRunRequest"/> (signed
    /// when <see cref="RemoteRunOptions.SigningKey"/> is set, retried on transient statuses), and
    /// waits for the remote train to finish. The output is read into
    /// <paramref name="outputType"/>, or, when that is an interface or abstract type, into the
    /// loaded implementation of it that the response names.
    /// </summary>
    /// <param name="trainName">The canonical name of the train the remote endpoint runs.</param>
    /// <param name="input">The train input; serialized using its runtime type.</param>
    /// <param name="outputType">The output type the caller expects.</param>
    /// <param name="ct">Cancels the HTTP call, including waits between retries.</param>
    /// <returns>The remote run's metadata id, external id (empty when none) and output.</returns>
    /// <exception cref="RemoteRunException">
    /// The endpoint answered with a non-success status after retries, with an empty body, with a
    /// body larger than <see cref="RemoteRunOptions.MaxResponseBodyBytes"/>, or, with a signing
    /// key, with a response not signed for this request.
    /// </exception>
    /// <exception cref="TrainException">
    /// The remote train failed (rebuilt by <see cref="RemoteRunResponse.ToTrainException"/>), or
    /// the output type the response names is not a loaded implementation of
    /// <paramref name="outputType"/>.
    /// </exception>
    /// <exception cref="HttpRequestException">
    /// The endpoint could not be reached. Only transient status codes are retried, not connection
    /// failures.
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

        var request = new RemoteRunRequest(trainName, inputJson, input.GetType().FullName!);

        string? requestSignature = null;
        using var httpResponse = await HttpRetryHelper.PostWithRetryAsync(
            httpClient,
            request,
            options.Retry,
            logger,
            ct,
            options.SigningKey,
            RunnerRequestPurpose.Run,
            signature => requestSignature = signature,
            HttpCompletionOption.ResponseHeadersRead
        );

        if (!httpResponse.IsSuccessStatusCode)
        {
            // The status and the reply are unauthenticated: the response signature covers a
            // success body only, so this is only ever a failure, and the reply goes into Message
            // only, capped. A client is shown PublicMessage, which is null here, so the GraphQL
            // error filter answers "The train failed." (central docs/0028); Message reaches the
            // caller's log, and the failure recorded for a train that made this run from a
            // junction, which operators read.
            var body = await ReadErrorBodyAsync(httpResponse, ct);
            throw new RemoteRunException(
                $"Remote run endpoint returned HTTP {(int)httpResponse.StatusCode}: {body}"
            );
        }

        // Read up to the limit before anything else looks at it, the signature included, so an
        // answer of any size costs at most MaxResponseBodyBytes.
        var (responseBody, tooLarge) = await BoundedBody.ReadAsync(
            httpResponse.Content,
            options.MaxResponseBodyBytes,
            httpClient.Timeout,
            ct
        );
        if (tooLarge)
            throw new RemoteRunException(
                "The remote run endpoint's response is larger than "
                    + $"RemoteRunOptions.MaxResponseBodyBytes ({options.MaxResponseBodyBytes} "
                    + "bytes), so it was not read."
            );

        // With a signing key, only an answer the runner signed for this request is read: the
        // signature covers the body and is bound to the nonce of the attempt it answers, so an
        // answer to another request does not verify. Without one the runner answers unsigned.
        if (options.SigningKey is { } key)
        {
            var responseSignature = httpResponse.Headers.TryGetValues(
                RunnerRequestSignature.ResponseHeaderName,
                out var values
            )
                ? values.FirstOrDefault()
                : null;

            if (
                requestSignature is null
                || !RunnerRequestSignature.VerifyResponse(
                    key,
                    RunnerRequestPurpose.Run,
                    requestSignature,
                    responseSignature,
                    responseBody
                )
            )
                throw new RemoteRunException(
                    "The remote run endpoint's response is not signed for this request ("
                        + (
                            responseSignature is null
                                ? $"it has no {RunnerRequestSignature.ResponseHeaderName} header"
                                : "its signature does not match"
                        )
                        + "). A runner with the same SigningKey signs every run response; check "
                        + "that the runner is on a release that does, and that both sides share "
                        + "the key."
                );
        }

        var response =
            JsonSerializer.Deserialize<RemoteRunResponse>(responseBody, RemoteRunJson.Read)
            ?? throw new RemoteRunException("Remote run endpoint returned null response.");

        if (response.IsError)
            throw BuildExceptionFromErrorResponse(response);

        // Read into the output type the caller expects, or, when that is an interface or abstract
        // type, into the loaded implementation of it the response names; never a type loaded by name.
        var output = RemoteRunOutput.Read(response.OutputJson, outputType, response.OutputType);

        return new RunTrainResult(response.MetadataId, response.ExternalId ?? "", output);
    }

    /// <summary>Kept for the tests that call it; see <see cref="RemoteRunResponse.ToTrainException"/>.</summary>
    internal static TrainException BuildExceptionFromErrorResponse(RemoteRunResponse response) =>
        response.ToTrainException();

    // Only the start of an error body is read: it is unauthenticated, and only ever logged.
    private async Task<string> ReadErrorBodyAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        try
        {
            var (bytes, exceeded) = await BoundedBody.ReadAsync(
                response.Content,
                MaxErrorBodyLength * 4L,
                httpClient.Timeout,
                ct,
                prefix: true
            );
            var body = System.Text.Encoding.UTF8.GetString(bytes);

            if (string.IsNullOrWhiteSpace(body))
                return response.ReasonPhrase ?? "no response body";

            return exceeded || body.Length > MaxErrorBodyLength
                ? body[..Math.Min(body.Length, MaxErrorBodyLength)] + "... (truncated)"
                : body;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return response.ReasonPhrase ?? "unable to read response body";
        }
    }
}
