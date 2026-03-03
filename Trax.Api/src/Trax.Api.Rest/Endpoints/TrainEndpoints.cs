using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trax.Api.DTOs;
using Trax.Api.Exceptions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Api.Rest.Endpoints;

public static class TrainEndpoints
{
    public static RouteGroupBuilder MapTrainEndpoints(this RouteGroupBuilder group)
    {
        var trains = group.MapGroup("/trains").WithTags("Trains");

        trains.MapGet("/", GetTrains).WithName("GetTrains");
        trains.MapPost("/queue", QueueTrain).WithName("QueueTrain");
        trains.MapPost("/run", RunTrain).WithName("RunTrain");

        return group;
    }

    private static IResult GetTrains(ITrainDiscoveryService discoveryService)
    {
        var registrations = discoveryService.DiscoverTrains();

        var trainInfos = registrations
            .Select(r => new TrainInfo(
                r.ServiceTypeName,
                r.ImplementationTypeName,
                r.InputTypeName,
                r.OutputTypeName,
                r.Lifetime.ToString(),
                GetInputSchema(r.InputType),
                r.RequiredPolicies,
                r.RequiredRoles
            ))
            .ToList();

        return Results.Ok(trainInfos);
    }

    private static async Task<IResult> QueueTrain(
        QueueTrainRequest request,
        ITrainExecutionService executionService,
        CancellationToken ct
    )
    {
        try
        {
            var inputJson = request.Input.GetRawText();
            var result = await executionService.QueueAsync(
                request.TrainName,
                inputJson,
                request.Priority ?? 0,
                ct
            );

            return Results.Ok(new QueueTrainResponse(result.WorkQueueId, result.ExternalId));
        }
        catch (TrainAuthorizationException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: 403);
        }
    }

    private static async Task<IResult> RunTrain(
        RunTrainRequest request,
        ITrainExecutionService executionService,
        CancellationToken ct
    )
    {
        try
        {
            var inputJson = request.Input.GetRawText();
            var result = await executionService.RunAsync(request.TrainName, inputJson, ct);

            return Results.Ok(new RunTrainResponse(result.MetadataId));
        }
        catch (TrainAuthorizationException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: 403);
        }
    }

    private static List<InputPropertySchema> GetInputSchema(Type inputType)
    {
        return inputType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .Select(p => new InputPropertySchema(
                p.Name,
                GetFriendlyTypeName(p.PropertyType),
                Nullable.GetUnderlyingType(p.PropertyType) is not null
                    || !p.PropertyType.IsValueType
            ))
            .ToList();
    }

    private static string GetFriendlyTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return $"{GetFriendlyTypeName(underlying)}?";

        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name[..type.Name.IndexOf('`')];
        var args = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
        return $"{name}<{args}>";
    }
}
