using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Http;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Services.RunExecutor;

/// <summary>
/// HTTP implementation of <see cref="IRunExecutor"/> that dispatches synchronous run requests
/// to a remote endpoint and blocks until the train completes.
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
public class HttpRunExecutor(
    HttpClient httpClient,
    RemoteRunOptions options,
    ILogger<HttpRunExecutor> logger
) : IRunExecutor
{
    private const int MaxErrorBodyLength = 2000;

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
            ct
        );

        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await ReadErrorBodyAsync(httpResponse);
            throw new TrainException(
                $"Remote run endpoint returned HTTP {(int)httpResponse.StatusCode}: {body}"
            );
        }

        var response =
            await httpResponse.Content.ReadFromJsonAsync<RemoteRunResponse>(ct)
            ?? throw new TrainException("Remote run endpoint returned null response.");

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

    /// <summary>
    /// Builds a <see cref="TrainException"/> from a <see cref="RemoteRunResponse"/> error.
    /// If the response includes structured error fields (<see cref="RemoteRunResponse.ExceptionType"/>
    /// and <see cref="RemoteRunResponse.FailureJunction"/>), reconstructs a <see cref="TrainExceptionData"/>
    /// JSON message so that <c>Metadata.AddException()</c> on the API side correctly parses
    /// the failure into structured fields (FailureException, FailureJunction, FailureReason).
    /// </summary>
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
