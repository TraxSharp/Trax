using System.Net.Http.Json;
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
    /// The endpoint answered with a non-success status after retries, or with an empty body.
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

        using var httpResponse = await HttpRetryHelper.PostWithRetryAsync(
            httpClient,
            request,
            options.Retry,
            logger,
            ct,
            options.SigningKey,
            RunnerRequestPurpose.Run
        );

        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await ReadErrorBodyAsync(httpResponse);
            throw new RemoteRunException(
                $"Remote run endpoint returned HTTP {(int)httpResponse.StatusCode}: {body}"
            );
        }

        var response =
            await httpResponse.Content.ReadFromJsonAsync<RemoteRunResponse>(RemoteRunJson.Read, ct)
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

    private static async Task<string> ReadErrorBodyAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(body))
                return response.ReasonPhrase ?? "no response body";

            return body.Length > MaxErrorBodyLength
                ? body[..MaxErrorBodyLength] + "... (truncated)"
                : body;
        }
        catch
        {
            return response.ReasonPhrase ?? "unable to read response body";
        }
    }
}
