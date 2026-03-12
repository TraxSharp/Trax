using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Services.RequestHandler;

/// <summary>
/// Default implementation of <see cref="ITraxRequestHandler"/>.
/// </summary>
internal class TraxRequestHandler(
    IJobRunnerTrain jobRunnerTrain,
    ITrainExecutionService executionService,
    ILogger<TraxRequestHandler> logger
) : ITraxRequestHandler
{
    public async Task<ExecuteJobResult> ExecuteJobAsync(
        RemoteJobRequest request,
        CancellationToken ct = default
    )
    {
        object? deserializedInput = null;
        if (request.Input is not null && request.InputType is not null)
        {
            var type = TypeResolver.ResolveType(request.InputType);
            deserializedInput = JsonSerializer.Deserialize(
                request.Input,
                type,
                TraxJsonSerializationOptions.ManifestProperties
            );
        }

        var jobRequest = deserializedInput is not null
            ? new RunJobRequest(request.MetadataId, deserializedInput)
            : new RunJobRequest(request.MetadataId);

        await jobRunnerTrain.Run(jobRequest, ct);

        return new ExecuteJobResult(request.MetadataId);
    }

    public async Task<RemoteRunResponse> RunTrainAsync(
        RemoteRunRequest request,
        CancellationToken ct = default
    )
    {
        try
        {
            var result = await executionService.RunAsync(request.TrainName, request.InputJson, ct);

            string? outputJson = null;
            string? outputType = null;

            if (result.Output is not null)
            {
                outputType = result.Output.GetType().FullName;
                outputJson = JsonSerializer.Serialize(
                    result.Output,
                    result.Output.GetType(),
                    TraxJsonSerializationOptions.ManifestProperties
                );
            }

            return new RemoteRunResponse(
                result.MetadataId,
                result.ExternalId,
                outputJson,
                outputType
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run execution failed for train {TrainName}", request.TrainName);

            return new RemoteRunResponse(MetadataId: 0, IsError: true, ErrorMessage: ex.Message);
        }
    }
}
