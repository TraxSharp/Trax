using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.DormantDependentContext;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;
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
    /// Registers the minimal DI services needed to run <see cref="JobRunnerTrain"/>,
    /// including <see cref="ITraxRequestHandler"/> for hosting-agnostic request handling.
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

        // Hosting-agnostic request handler
        services.AddScoped<ITraxRequestHandler, TraxRequestHandler>();

        return services;
    }

    /// <summary>
    /// Maps a POST endpoint that receives and executes job requests from a remote scheduler.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder</param>
    /// <param name="route">The route to map (default: "/trax/execute")</param>
    /// <returns>The route handler builder for further configuration (e.g., <c>.RequireAuthorization()</c>)</returns>
    /// <remarks>
    /// Delegates to <see cref="ITraxRequestHandler.ExecuteJobAsync"/> for the actual execution.
    /// Returns a <see cref="RemoteJobResponse"/> with structured error fields on failure.
    /// No authentication is baked in — apply your own ASP.NET middleware as needed.
    /// </remarks>
    public static RouteHandlerBuilder UseTraxJobRunner(
        this IEndpointRouteBuilder endpoints,
        string route = "/trax/execute"
    )
    {
        return endpoints.MapPost(
            route,
            async (
                RemoteJobRequest request,
                ITraxRequestHandler handler,
                ILogger<JobRunnerTrain> logger
            ) =>
            {
                try
                {
                    var result = await handler.ExecuteJobAsync(request);
                    return Results.Ok(new RemoteJobResponse(result.MetadataId));
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Remote job execution failed for Metadata {MetadataId}",
                        request.MetadataId
                    );
                    return Results.Ok(
                        new RemoteJobResponse(
                            request.MetadataId,
                            IsError: true,
                            ErrorMessage: ex.Message,
                            ExceptionType: ex.GetType().Name,
                            StackTrace: ex.StackTrace
                        )
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
    /// Delegates to <see cref="ITraxRequestHandler.RunTrainAsync"/> for the actual execution.
    /// Unlike <see cref="UseTraxJobRunner"/> which is fire-and-forget (queue path), this endpoint
    /// blocks until the train completes and returns the serialized output in the response body.
    /// Returns a <see cref="RemoteRunResponse"/> with structured error fields on failure.
    /// No authentication is baked in — apply your own ASP.NET middleware as needed.
    /// </remarks>
    public static RouteHandlerBuilder UseTraxRunEndpoint(
        this IEndpointRouteBuilder endpoints,
        string route = "/trax/run"
    )
    {
        return endpoints.MapPost(
            route,
            async (
                RemoteRunRequest request,
                ITraxRequestHandler handler,
                ILogger<TraxRequestHandler> logger
            ) =>
            {
                try
                {
                    return Results.Ok(await handler.RunTrainAsync(request));
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Remote run execution failed for train {TrainName}",
                        request.TrainName
                    );
                    return Results.Ok(TraxRequestHandler.BuildErrorResponse(ex));
                }
            }
        );
    }
}
