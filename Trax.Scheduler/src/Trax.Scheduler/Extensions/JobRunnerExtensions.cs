using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Extensions;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.DormantDependentContext;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Extension methods for setting up a remote job runner endpoint.
/// </summary>
/// <remarks>
/// These methods register the minimal services needed to run <see cref="JobRunnerTrain"/>
/// without the full scheduler (no ManifestManager, no JobDispatcher, no polling services).
/// Use this on the remote side — the process that receives and executes jobs dispatched
/// by a scheduler configured with <c>UseRemoteWorkers()</c>.
/// </remarks>
public static class JobRunnerExtensions
{
    /// <summary>
    /// Registers the minimal DI services needed to run <see cref="JobRunnerTrain"/>.
    /// </summary>
    /// <remarks>
    /// This registers only the execution pipeline — no scheduling, no dispatching, no polling.
    /// The remote process must also call <c>AddTrax()</c> with <c>AddEffects()</c>,
    /// <c>UsePostgres()</c>, and <c>AddMediator()</c> to register the effect system and train assemblies.
    /// </remarks>
    public static IServiceCollection AddTraxJobRunner(this IServiceCollection services)
    {
        // Empty scheduler configuration (no manifests, no polling)
        services.AddSingleton(new SchedulerConfiguration());

        // Cancellation registry (singleton — shared across all requests)
        services.AddSingleton<ICancellationRegistry, CancellationRegistry>();

        // Runtime scheduler interface
        services.AddScoped<ITraxScheduler, TraxScheduler>();

        // Dependent train context
        services.AddScoped<DormantDependentContext>();
        services.AddScoped<IDormantDependentContext>(sp =>
            sp.GetRequiredService<DormantDependentContext>()
        );

        // JobRunnerTrain (uses AddScopedTraxRoute for property injection)
        services.AddScopedTraxRoute<IJobRunnerTrain, JobRunnerTrain>();

        return services;
    }

    /// <summary>
    /// Maps a POST endpoint that receives and executes job requests from a remote scheduler.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder</param>
    /// <param name="route">The route to map (default: "/trax/execute")</param>
    /// <returns>The route handler builder for further configuration (e.g., <c>.RequireAuthorization()</c>)</returns>
    /// <remarks>
    /// The endpoint receives a <see cref="RemoteJobRequest"/> JSON payload, deserializes the input,
    /// and runs <see cref="JobRunnerTrain"/> to completion. No authentication is baked in —
    /// apply your own ASP.NET middleware as needed.
    /// </remarks>
    public static RouteHandlerBuilder UseTraxJobRunner(
        this IEndpointRouteBuilder endpoints,
        string route = "/trax/execute"
    )
    {
        return endpoints.MapPost(
            route,
            async (RemoteJobRequest request, IServiceProvider sp) =>
            {
                var logger = sp.GetRequiredService<ILogger<JobRunnerTrain>>();

                try
                {
                    object? deserializedInput = null;
                    if (request.Input is not null && request.InputType is not null)
                    {
                        var type = ResolveType(request.InputType);
                        deserializedInput = JsonSerializer.Deserialize(
                            request.Input,
                            type,
                            TraxJsonSerializationOptions.ManifestProperties
                        );
                    }

                    var train = sp.GetRequiredService<IJobRunnerTrain>();

                    var jobRequest = deserializedInput is not null
                        ? new RunJobRequest(request.MetadataId, deserializedInput)
                        : new RunJobRequest(request.MetadataId);

                    await train.Run(jobRequest, CancellationToken.None);

                    return Results.Ok(new { metadataId = request.MetadataId });
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Remote job execution failed for Metadata {MetadataId}",
                        request.MetadataId
                    );
                    return Results.Problem(
                        detail: ex.Message,
                        statusCode: StatusCodes.Status500InternalServerError
                    );
                }
            }
        );
    }

    /// <summary>
    /// Maps a POST endpoint that receives synchronous run requests and returns the train output.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder</param>
    /// <param name="route">The route to map (default: "/trax/run")</param>
    /// <returns>The route handler builder for further configuration (e.g., <c>.RequireAuthorization()</c>)</returns>
    /// <remarks>
    /// Unlike <see cref="UseTraxJobRunner"/> which is fire-and-forget (queue path), this endpoint
    /// blocks until the train completes and returns the serialized output in the response body.
    /// The caller receives a <see cref="RemoteRunResponse"/> with the train output or error details.
    ///
    /// Set up the calling side with <c>UseRemoteRun()</c> on the scheduler builder.
    /// No authentication is baked in — apply your own ASP.NET middleware as needed.
    /// </remarks>
    public static RouteHandlerBuilder UseTraxRunEndpoint(
        this IEndpointRouteBuilder endpoints,
        string route = "/trax/run"
    )
    {
        return endpoints.MapPost(
            route,
            async (RemoteRunRequest request, IServiceProvider sp) =>
            {
                var logger = sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Trax.RunEndpoint");

                try
                {
                    var executionService = sp.GetRequiredService<ITrainExecutionService>();

                    var result = await executionService.RunAsync(
                        request.TrainName,
                        request.InputJson,
                        CancellationToken.None
                    );

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

                    return Results.Ok(
                        new RemoteRunResponse(result.MetadataId, outputJson, outputType)
                    );
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Remote run execution failed for train {TrainName}",
                        request.TrainName
                    );

                    return Results.Ok(
                        new RemoteRunResponse(
                            MetadataId: 0,
                            IsError: true,
                            ErrorMessage: ex.Message
                        )
                    );
                }
            }
        );
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

        throw new TypeLoadException($"Unable to find type: {typeName}");
    }
}
