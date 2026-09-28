using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.DormantDependentContext;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RequestSigning;
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
    /// including <see cref="ITraxRequestHandler"/> for hosting-agnostic request handling,
    /// with no runner posture configured.
    /// </summary>
    /// <remarks>
    /// Enough for a host that needs <see cref="ITraxScheduler"/> without mapping a runner endpoint.
    /// A host that maps <see cref="UseTraxJobRunner"/> or <see cref="UseTraxRunEndpoint"/>, or runs
    /// an SQS or Lambda runner, calls the overload that configures <see cref="TraxJobRunnerOptions"/>;
    /// without a posture those entry points refuse to start.
    /// </remarks>
    public static IServiceCollection AddTraxJobRunner(this IServiceCollection services) =>
        services.AddTraxJobRunner(_ => { });

    /// <summary>
    /// Registers the minimal DI services needed to run <see cref="JobRunnerTrain"/>,
    /// including <see cref="ITraxRequestHandler"/> for hosting-agnostic request handling,
    /// and the posture every runner entry point enforces.
    /// </summary>
    /// <remarks>
    /// This registers only the execution pipeline — no scheduling, no dispatching, no polling.
    /// The remote process must also call <c>AddTrax()</c> with <c>AddEffects()</c>,
    /// <c>UsePostgres()</c>, and <c>AddMediator()</c> to register the effect system and train assemblies.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Sets the posture: a <see cref="TraxJobRunnerOptions.SigningKey"/> shared with the scheduler,
    /// an <see cref="TraxJobRunnerOptions.AuthorizationPolicy"/>, or
    /// <see cref="TraxJobRunnerOptions.AllowUnsignedRequests"/>.
    /// </param>
    public static IServiceCollection AddTraxJobRunner(
        this IServiceCollection services,
        Action<TraxJobRunnerOptions> configure
    )
    {
        ArgumentNullException.ThrowIfNull(configure);

        var runnerOptions = new TraxJobRunnerOptions();
        configure(runnerOptions);
        runnerOptions.Validate();

        services.AddSingleton(runnerOptions);
        services.TryAddSingleton<INonceStore>(sp => CreateNonceStore(sp, runnerOptions));
        services.AddSingleton(sp => new RunnerRequestVerifier(
            runnerOptions,
            sp.GetRequiredService<ILogger<RunnerRequestVerifier>>(),
            // Only a signing key has nonces to keep, so a runner without one needs no store.
            runnerOptions.SigningKey
                is null
                ? null
                : sp.GetRequiredService<INonceStore>(),
            sp.GetService<TimeProvider>()
        ));

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
    /// The store a signing runner keeps accepted nonces in: the database, which every instance of
    /// the runner shares, unless the host chose memory (see scheduler/0009).
    /// </summary>
    private static INonceStore CreateNonceStore(
        IServiceProvider services,
        TraxJobRunnerOptions runnerOptions
    )
    {
        if (runnerOptions.InMemoryNonceStore)
            return new InMemoryNonceStore();

        if (
            services.GetService<ISqlDialect>() is not { } dialect
            || services.GetService<IDataContextProviderFactory>() is not { } contexts
        )
            throw new InvalidOperationException(
                "A Trax runner with a SigningKey keeps the nonces it accepts in the database, so that "
                    + "every instance of the runner refuses a repeated request, and this host has no "
                    + "relational data provider (UsePostgres or UseSqlite). Add one, register an "
                    + "INonceStore, or call UseInMemoryNonceStore() if the runner runs as one instance."
            );

        return new DatabaseNonceStore(contexts, dialect);
    }

    /// <summary>
    /// Maps a POST endpoint that receives and executes job requests from a remote scheduler.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder</param>
    /// <param name="route">The route to map (default: "/trax/execute")</param>
    /// <returns>The route handler builder for further configuration</returns>
    /// <remarks>
    /// Delegates to <see cref="ITraxRequestHandler.ExecuteJobAsync"/> for the actual execution.
    /// Enforces the posture configured by <c>AddTraxJobRunner(runner => ...)</c>, and throws
    /// while mapping when there is none: a signed request is checked before its body is read as
    /// an envelope, and an <see cref="TraxJobRunnerOptions.AuthorizationPolicy"/> is applied to
    /// the endpoint. Returns a <see cref="RemoteJobResponse"/> with structured error fields on
    /// failure; the detail stays in this process's log.
    /// </remarks>
    public static RouteHandlerBuilder UseTraxJobRunner(
        this IEndpointRouteBuilder endpoints,
        string route = "/trax/execute"
    )
    {
        var runnerOptions = RequirePosture(endpoints, route);

        var builder = endpoints
            .MapPost(
                route,
                async (
                    HttpRequest httpRequest,
                    ITraxRequestHandler handler,
                    RunnerRequestVerifier verifier,
                    ILogger<JobRunnerTrain> logger
                ) =>
                {
                    var (request, refused) = await ReadEnvelopeAsync<RemoteJobRequest>(
                        httpRequest,
                        verifier,
                        RunnerRequestPurpose.Execute,
                        logger
                    );
                    if (request is null)
                        return refused!;

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
                        return Results.Ok(BuildJobErrorResponse(request.MetadataId, ex));
                    }
                }
            )
            .Accepts<RemoteJobRequest>("application/json");

        return ApplyPolicy(builder, runnerOptions);
    }

    /// <summary>
    /// Maps a POST endpoint that receives synchronous run requests and returns the train output.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder</param>
    /// <param name="route">The route to map (default: "/trax/run")</param>
    /// <returns>The route handler builder for further configuration</returns>
    /// <remarks>
    /// Delegates to <see cref="ITraxRequestHandler.RunTrainAsync"/> for the actual execution.
    /// Unlike <see cref="UseTraxJobRunner"/> which is fire-and-forget (queue path), this endpoint
    /// blocks until the train completes and returns the serialized output in the response body.
    /// Enforces the same posture as <see cref="UseTraxJobRunner"/>, and throws while mapping when
    /// there is none. Returns a <see cref="RemoteRunResponse"/> with structured error fields on failure.
    /// </remarks>
    public static RouteHandlerBuilder UseTraxRunEndpoint(
        this IEndpointRouteBuilder endpoints,
        string route = "/trax/run"
    )
    {
        var runnerOptions = RequirePosture(endpoints, route);

        var builder = endpoints
            .MapPost(
                route,
                async (
                    HttpRequest httpRequest,
                    ITraxRequestHandler handler,
                    RunnerRequestVerifier verifier,
                    ILogger<TraxRequestHandler> logger
                ) =>
                {
                    var (request, refused) = await ReadEnvelopeAsync<RemoteRunRequest>(
                        httpRequest,
                        verifier,
                        RunnerRequestPurpose.Run,
                        logger
                    );
                    if (request is null)
                        return refused!;

                    try
                    {
                        return Results.Json(
                            await handler.RunTrainAsync(request),
                            RemoteRunJson.Write
                        );
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "Remote run execution failed for train {TrainName}",
                            request.TrainName
                        );
                        return Results.Json(
                            TraxRequestHandler.BuildErrorResponse(ex),
                            RemoteRunJson.Write
                        );
                    }
                }
            )
            .Accepts<RemoteRunRequest>("application/json");

        return ApplyPolicy(builder, runnerOptions);
    }

    /// <summary>
    /// The error a queued-job entry point reports back. A <see cref="TrainException"/>'s message is
    /// Trax's own account of the failure and travels; anything else stays in this process's log.
    /// </summary>
    internal static RemoteJobResponse BuildJobErrorResponse(long metadataId, Exception ex) =>
        new(
            metadataId,
            IsError: true,
            ErrorMessage: ex is TrainException
                ? ex.Message
                : TraxRequestHandler.UnreportedFailureMessage,
            ExceptionType: ex.GetType().Name
        );

    private static TraxJobRunnerOptions RequirePosture(
        IEndpointRouteBuilder endpoints,
        string route
    )
    {
        var verifier =
            endpoints.ServiceProvider.GetService<RunnerRequestVerifier>()
            ?? throw new InvalidOperationException(
                $"Mapping the Trax runner endpoint '{route}' requires AddTraxJobRunner(runner => ...) "
                    + "with a SigningKey, an AuthorizationPolicy, or AllowUnsignedRequests()."
            );

        verifier.EnsurePosture(route, policyApplies: true);
        return endpoints.ServiceProvider.GetRequiredService<TraxJobRunnerOptions>();
    }

    private static RouteHandlerBuilder ApplyPolicy(
        RouteHandlerBuilder builder,
        TraxJobRunnerOptions runnerOptions
    ) =>
        runnerOptions.AuthorizationPolicy is { } policy
            ? builder.RequireAuthorization(policy)
            : builder;

    /// <summary>
    /// How the two endpoints read their envelope: the web defaults ASP.NET binds with, plus a
    /// refusal of repeated properties. Scoped to these endpoints rather than set on the host's
    /// JSON options, which govern every other endpoint the host maps.
    /// </summary>
    private static readonly JsonSerializerOptions EnvelopeOptions = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
    };

    /// <summary>
    /// Reads the request body as <typeparamref name="T"/>, or returns the response that refuses
    /// it: 415 for a body that is not JSON, 401 for one whose signature the posture refuses, 400
    /// for one that does not parse, repeats a property (in any case), or is <c>null</c>.
    /// </summary>
    private static async Task<(T? Envelope, IResult? Refused)> ReadEnvelopeAsync<T>(
        HttpRequest request,
        RunnerRequestVerifier verifier,
        RunnerRequestPurpose purpose,
        ILogger logger
    )
        where T : class
    {
        if (!request.HasJsonContentType())
            return (null, Results.StatusCode(StatusCodes.Status415UnsupportedMediaType));

        // The signature covers the exact bytes, so they are read once and both checked and parsed.
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, request.HttpContext.RequestAborted);
        var body = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);

        var verdict = await verifier.VerifyAsync(
            purpose,
            body,
            request.Headers[RunnerRequestSignature.HeaderName].ToString(),
            requireFresh: true,
            request.HttpContext.RequestAborted
        );
        if (verdict != RunnerRequestVerdict.Accepted)
        {
            logger.LogWarning(
                "Refused a Trax runner request to {Path}: signature {Verdict}",
                request.Path,
                verdict
            );
            return (null, Results.StatusCode(StatusCodes.Status401Unauthorized));
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<T>(body.Span, EnvelopeOptions);

            return envelope is null ? (null, Results.BadRequest()) : (envelope, null);
        }
        catch (JsonException)
        {
            return (null, Results.BadRequest());
        }
    }
}
