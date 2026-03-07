using System.Net.Http.Json;
using System.Text.Json;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainExecution;

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
/// </remarks>
public class HttpRunExecutor(HttpClient httpClient) : IRunExecutor
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

        var request = new RemoteRunRequest(trainName, inputJson, input.GetType().FullName!);

        var httpResponse = await httpClient.PostAsJsonAsync(string.Empty, request, ct);
        httpResponse.EnsureSuccessStatusCode();

        var response =
            await httpResponse.Content.ReadFromJsonAsync<RemoteRunResponse>(ct)
            ?? throw new TrainException("Remote run endpoint returned null response.");

        if (response.IsError)
            throw new TrainException($"Remote train execution failed: {response.ErrorMessage}");

        object? output = null;
        if (response.OutputJson is not null && response.OutputType is not null)
        {
            var resolvedType = ResolveType(response.OutputType);
            output = JsonSerializer.Deserialize(
                response.OutputJson,
                resolvedType,
                TraxJsonSerializationOptions.ManifestProperties
            );
        }

        return new RunTrainResult(response.MetadataId, output);
    }

    private static Type ResolveType(string typeName)
    {
        var type = Type.GetType(typeName);
        if (type is not null)
            return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName);
            if (type is not null)
                return type;
        }

        throw new TypeLoadException(
            $"Unable to resolve output type from remote run response: {typeName}"
        );
    }
}
