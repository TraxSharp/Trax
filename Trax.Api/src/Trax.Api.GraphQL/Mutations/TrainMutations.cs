using System.Text.Json;
using HotChocolate.Types;
using Trax.Api.DTOs;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Api.GraphQL.Mutations;

[ExtendObjectType(OperationTypeNames.Mutation)]
public class TrainMutations
{
    public async Task<QueueTrainResponse> QueueTrain(
        string trainName,
        JsonElement input,
        int? priority,
        [Service] ITrainExecutionService executionService,
        CancellationToken ct
    )
    {
        var inputJson = input.GetRawText();
        var result = await executionService.QueueAsync(trainName, inputJson, priority ?? 0, ct);
        return new QueueTrainResponse(result.WorkQueueId, result.ExternalId);
    }

    public async Task<RunTrainResponse> RunTrain(
        string trainName,
        JsonElement input,
        [Service] ITrainExecutionService executionService,
        CancellationToken ct
    )
    {
        var inputJson = input.GetRawText();
        var result = await executionService.RunAsync(trainName, inputJson, ct);
        return new RunTrainResponse(result.MetadataId);
    }
}
