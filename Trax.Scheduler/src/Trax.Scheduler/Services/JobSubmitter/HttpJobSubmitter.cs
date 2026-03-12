using System.Net.Http.Json;
using System.Text.Json;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Services.JobSubmitter;

/// <summary>
/// HTTP implementation of <see cref="IJobSubmitter"/> that dispatches jobs to a remote endpoint.
/// </summary>
/// <remarks>
/// Used by <c>UseRemoteWorkers()</c>. Serializes a <see cref="RemoteJobRequest"/> as JSON
/// and POSTs it to the configured <see cref="RemoteWorkerOptions.BaseUrl"/>.
/// The remote endpoint runs <see cref="Trains.JobRunner.JobRunnerTrain"/> to execute the train.
/// </remarks>
public class HttpJobSubmitter(HttpClient httpClient) : IJobSubmitter
{
    private const int MaxErrorBodyLength = 2000;

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
        await PostAsync(request, cancellationToken);
        return $"http-{Guid.NewGuid():N}";
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
        await PostAsync(request, cancellationToken);
        return $"http-{Guid.NewGuid():N}";
    }

    private async Task PostAsync(RemoteJobRequest request, CancellationToken cancellationToken)
    {
        var httpResponse = await httpClient.PostAsJsonAsync(
            string.Empty,
            request,
            cancellationToken
        );

        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await ReadErrorBodyAsync(httpResponse);
            throw new TrainException(
                $"Remote worker returned HTTP {(int)httpResponse.StatusCode}: {body}"
            );
        }

        RemoteJobResponse? response;
        try
        {
            response = await httpResponse.Content.ReadFromJsonAsync<RemoteJobResponse>(
                cancellationToken
            );
        }
        catch
        {
            // Response body is not valid RemoteJobResponse JSON — treat as success
            // (e.g., older runner returning { metadataId: 123 } without IsError field)
            return;
        }

        if (response is { IsError: true })
        {
            throw new TrainException(
                $"Remote worker reported error: {response.ErrorMessage}"
                    + (
                        response.ExceptionType is not null
                            ? $" [{response.ExceptionType}]"
                            : string.Empty
                    )
            );
        }
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
