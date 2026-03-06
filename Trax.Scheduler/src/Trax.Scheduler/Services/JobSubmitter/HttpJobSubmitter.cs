using System.Net.Http.Json;
using System.Text.Json;
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
        var response = await httpClient.PostAsJsonAsync(string.Empty, request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
