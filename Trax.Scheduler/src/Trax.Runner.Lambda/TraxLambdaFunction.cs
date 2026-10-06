using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Lambda;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RequestSigning;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Runner.Lambda;

/// <summary>
/// Base class for AWS Lambda functions that execute Trax trains via direct SDK invocation.
/// </summary>
/// <remarks>
/// <para>
/// Override <see cref="ConfigureServices"/> to register your data contexts, Trax effects,
/// and train assemblies. The base class automatically registers logging,
/// <c>IConfiguration</c> (from appsettings.json + environment variables),
/// and <c>AddTraxJobRunner()</c> — do not call these yourself.
/// </para>
///
/// <para>
/// Override <see cref="ConfigureRunner"/> to set the runner's posture: a <c>SigningKey</c> shared
/// with the scheduler's <c>UseLambdaWorkers</c> / <c>UseLambdaRun</c> options, or
/// <c>AllowUnsignedRequests()</c> for a function only the scheduler's IAM role can invoke. Without
/// either, every invocation is refused. A <c>Run</c> envelope's signature must be fresh and not
/// repeated; an <c>Execute</c> envelope's is checked for its signature only, because an
/// asynchronous invocation is retried with the same payload and the job's Pending metadata row is
/// what stops it running twice.
/// </para>
///
/// <para>
/// The Lambda function receives a <see cref="LambdaEnvelope"/> payload directly — no API Gateway
/// or Function URL is needed. The <see cref="LambdaEnvelope.Type"/> field determines whether
/// the request is a fire-and-forget job execution or a synchronous train run.
/// </para>
///
/// <para>
/// <b>Cold start optimization:</b> The service provider is built lazily on the first
/// invocation, not during Lambda container creation. The <see cref="ConfigureServices"/>
/// method controls the entire DI graph — keep it minimal for faster cold starts.
/// </para>
///
/// <para>
/// <b>Local development:</b> Use <see cref="RunLocalAsync(string[])"/> to run the function as a
/// local Kestrel web server for development and testing without AWS tooling.
/// </para>
///
/// <example>
/// <code>
/// [assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]
///
/// public class Function : TraxLambdaFunction
/// {
///     protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
///     {
///         var connString = configuration.GetConnectionString("TraxDatabase")!;
///
///         services.AddTrax(trax => trax
///             .AddEffects(e => e.UsePostgres(connString))
///             .AddMediator(typeof(MyTrain).Assembly));
///     }
/// }
/// </code>
/// </example>
/// </remarks>
public abstract class TraxLambdaFunction
{
    /// <summary>
    /// How an envelope's payload is read. A property repeated in the payload, in the same or a
    /// different case, is refused rather than resolved by whichever copy comes last.
    /// </summary>
    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowDuplicateProperties = false,
    };

    private readonly Lazy<IServiceProvider> _serviceProvider;

    /// <summary>
    /// Initializes the function without building its service provider; that is deferred to the
    /// first invocation (see <see cref="BuildServiceProvider"/>) to keep cold starts short.
    /// </summary>
    protected TraxLambdaFunction()
    {
        _serviceProvider = new Lazy<IServiceProvider>(BuildServiceProvider);
    }

    /// <summary>
    /// Override to register your Trax effects, mediator, data contexts, and application services.
    /// Do NOT call <c>AddTraxJobRunner()</c> — the base class registers it automatically.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <param name="configuration">Configuration loaded from appsettings.json and environment variables</param>
    protected abstract void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration
    );

    /// <summary>
    /// Override to set the runner's posture. Called by the default <see cref="BuildServiceProvider"/>
    /// after <see cref="ConfigureServices"/>. The default sets nothing, so every invocation is refused
    /// until a signing key or <c>AllowUnsignedRequests()</c> is configured.
    /// </summary>
    /// <param name="runner">The runner options to configure</param>
    /// <param name="configuration">Configuration loaded from appsettings.json and environment variables</param>
    /// <example>
    /// <code>
    /// protected override void ConfigureRunner(TraxJobRunnerOptions runner, IConfiguration configuration) =>
    ///     runner.SigningKey = Convert.FromBase64String(configuration["Trax:RunnerSigningKey"]!);
    /// </code>
    /// </example>
    protected virtual void ConfigureRunner(
        TraxJobRunnerOptions runner,
        IConfiguration configuration
    ) { }

    /// <summary>
    /// Override to customize logging. Default adds console logging at <see cref="Microsoft.Extensions.Logging.LogLevel.Information"/>.
    /// </summary>
    /// <param name="logging">The logging builder to configure</param>
    protected virtual void ConfigureLogging(ILoggingBuilder logging)
    {
        logging.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
    }

    /// <summary>
    /// How much of <see cref="ILambdaContext.RemainingTime"/> is held back so a run cancelled by
    /// the function timing out can still record its outcome.
    /// </summary>
    /// <remarks>
    /// Cancelling at <c>RemainingTime</c> itself cancels at the instant Lambda freezes or kills the
    /// environment, so the uncancellable terminal write has nowhere to happen: the row stays
    /// <c>InProgress</c> holding its subject until <c>StaleInProgressTimeout</c>, and the reaper
    /// then records <c>Failed</c> rather than <c>Cancelled</c>, which a manifest counts toward
    /// retries and dead letters. Widen this for a data provider with a slower write path.
    /// <para>
    /// At most half of the time left is held back, so a function whose timeout is at or below
    /// the margin (AWS's default is three seconds) still runs its work, with a warning logged once
    /// per instance that the margin was cut.
    /// </para>
    /// </remarks>
    protected virtual TimeSpan TerminalWriteMargin => TimeSpan.FromSeconds(5);

    private int _warnedMarginCut;

    /// <summary>
    /// Lambda entry point for direct SDK invocation.
    /// Receives a <see cref="LambdaEnvelope"/> and dispatches to the appropriate handler
    /// based on <see cref="LambdaEnvelope.Type"/>.
    /// Cancellation is derived from <see cref="ILambdaContext.RemainingTime"/>, less
    /// <see cref="TerminalWriteMargin"/> or half the time left, whichever is smaller. An
    /// <c>Execute</c> whose time runs out before its job starts records the job's run
    /// <c>Cancelled</c> rather than leaving it <c>Pending</c>.
    /// </summary>
    public async Task<object?> FunctionHandler(LambdaEnvelope envelope, ILambdaContext context)
    {
        var remaining =
            context.RemainingTime > TimeSpan.Zero ? context.RemainingTime : TimeSpan.Zero;
        var halfRemaining = remaining / 2;
        var margin = TerminalWriteMargin < halfRemaining ? TerminalWriteMargin : halfRemaining;
        using var cts = new CancellationTokenSource(remaining - margin);
        using var scope = _serviceProvider.Value.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ITraxRequestHandler>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TraxLambdaFunction>>();

        if (remaining <= TerminalWriteMargin && Interlocked.Exchange(ref _warnedMarginCut, 1) == 0)
            logger.LogWarning(
                "The function had {Remaining} left, no more than the terminal write margin of "
                    + "{Margin}; half of it is held back instead. Raise the function's timeout so "
                    + "a run cancelled by it can record its outcome",
                remaining,
                TerminalWriteMargin
            );

        var purpose = envelope.Type switch
        {
            LambdaRequestType.Execute => RunnerRequestPurpose.Execute,
            LambdaRequestType.Run => RunnerRequestPurpose.Run,
            _ => throw new InvalidOperationException(
                $"Unknown Lambda request type: {envelope.Type}"
            ),
        };

        // An Execute arrives by asynchronous invocation, which Lambda retries with the same
        // payload, so only its signature is checked; a Run is synchronous and must be fresh.
        var verdict = await RequireVerifier(scope.ServiceProvider)
            .VerifyAsync(
                purpose,
                Encoding.UTF8.GetBytes(envelope.PayloadJson),
                envelope.Signature,
                requireFresh: purpose == RunnerRequestPurpose.Run,
                cts.Token
            );
        if (verdict != RunnerRequestVerdict.Accepted)
        {
            logger.LogWarning(
                "Refused a {Type} invocation: signature {Verdict}",
                envelope.Type,
                verdict
            );
            throw new InvalidOperationException(
                $"Refused a {envelope.Type} invocation: signature {verdict}."
            );
        }

        return envelope.Type switch
        {
            LambdaRequestType.Execute => await HandleExecute(
                envelope.PayloadJson,
                handler,
                logger,
                cts.Token,
                recordCancelledIn: scope.ServiceProvider
            ),
            LambdaRequestType.Run => await HandleRun(
                envelope.PayloadJson,
                handler,
                logger,
                cts.Token
            ),
            _ => throw new InvalidOperationException(
                $"Unknown Lambda request type: {envelope.Type}"
            ),
        };
    }

    /// <summary>
    /// Runs the Lambda function as a local Kestrel web server for development.
    /// Maps <c>POST /trax/execute</c> and <c>POST /trax/run</c> endpoints that wrap
    /// incoming request bodies into <see cref="LambdaEnvelope"/> payloads and execute
    /// them through the same handler logic as the Lambda entry point.
    /// </summary>
    /// <remarks>
    /// The routes take their own posture. With a signing key, every request must carry a valid
    /// <c>Trax-Signature</c> header, checked before its body is read, from wherever it comes.
    /// Without one (<c>AllowUnsignedRequests()</c>, a posture meant for the Lambda invocation entry
    /// point, which only the scheduler's IAM role can reach), the routes serve only requests from
    /// this machine's loopback address and refuse any other with 401, whatever address the server
    /// listens on. A body larger than <c>MaxRequestBodyBytes</c> is refused with 413.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Program.cs
    /// await new Function().RunLocalAsync(args);
    /// </code>
    /// </example>
    /// <param name="args">Command-line arguments passed to the web application builder. The
    /// content root, where the server reads its <c>appsettings.json</c> (and so its Kestrel URLs),
    /// is the app's base directory, the one <see cref="BuildServiceProvider"/> reads its
    /// configuration from, wherever the process was started; a <c>--contentRoot</c> argument or
    /// a <c>DOTNET_CONTENTROOT</c> / <c>ASPNETCORE_CONTENTROOT</c> variable still sets it.</param>
    public Task RunLocalAsync(string[] args) => RunLocalAsync(args, CancellationToken.None);

    /// <summary>
    /// Runs the Lambda function as a local Kestrel web server for development, as
    /// <see cref="RunLocalAsync(string[])"/> does, until <paramref name="cancellationToken"/> is
    /// cancelled or the host shuts down.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the web application builder.</param>
    /// <param name="cancellationToken">Stops the server when cancelled; the returned task then
    /// completes once it has shut down.</param>
    public async Task RunLocalAsync(string[] args, CancellationToken cancellationToken)
    {
        await using var app = CreateLocalBuilder(args).Build();
        ConfigureRoutes(app);
        await Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.RunAsync(
            app,
            cancellationToken
        );
    }

    /// <summary>
    /// The builder <see cref="RunLocalAsync(string[], CancellationToken)"/> runs. Its content root
    /// is <see cref="AppContext.BaseDirectory"/> unless the arguments or the environment name one,
    /// so <c>dotnet run --project</c> from another directory reads the same
    /// <c>appsettings.json</c> as the function's own configuration.
    /// </summary>
    internal static WebApplicationBuilder CreateLocalBuilder(string[] args)
    {
        var named = new ConfigurationBuilder()
            .AddEnvironmentVariables("DOTNET_")
            .AddEnvironmentVariables("ASPNETCORE_")
            .AddCommandLine(args)
            .Build()["contentRoot"];

        return WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = string.IsNullOrEmpty(named) ? AppContext.BaseDirectory : null,
            }
        );
    }

    /// <summary>
    /// Maps the local-development HTTP endpoints (<c>POST /trax/execute</c>, <c>POST /trax/run</c>)
    /// onto the supplied route builder. Exposed for test hosting; production code uses
    /// <see cref="RunLocalAsync(string[], CancellationToken)"/>.
    /// </summary>
    internal void ConfigureRoutes(IEndpointRouteBuilder routes)
    {
        MapLocalRoute(
            routes,
            "/trax/execute",
            RunnerRequestPurpose.Execute,
            async (body, handler, logger, ct) => await HandleExecute(body, handler, logger, ct)
        );
        MapLocalRoute(
            routes,
            "/trax/run",
            RunnerRequestPurpose.Run,
            async (body, handler, logger, ct) => await HandleRun(body, handler, logger, ct)
        );
    }

    private void MapLocalRoute(
        IEndpointRouteBuilder routes,
        string route,
        RunnerRequestPurpose purpose,
        Func<string, ITraxRequestHandler, ILogger, CancellationToken, Task<object>> handle
    ) =>
        routes.MapPost(
            route,
            async (HttpContext ctx) =>
            {
                using var scope = _serviceProvider.Value.CreateScope();
                var verifier = RequireVerifier(scope.ServiceProvider);

                // Unsigned is a posture for the Lambda invocation entry point. Over HTTP it is
                // honoured only for a caller on this machine.
                if (!verifier.RequiresSignature && !IsFromThisMachine(ctx))
                {
                    verifier.ReportRefusal(
                        ctx.Request.Path,
                        "unsigned request from another machine; the local routes accept unsigned "
                            + "requests only from loopback"
                    );
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                var (body, refusedStatus) = await verifier.ReadVerifiedBodyAsync(
                    ctx.Request,
                    purpose
                );
                if (refusedStatus is { } status)
                {
                    ctx.Response.StatusCode = status;
                    return;
                }

                var handler = scope.ServiceProvider.GetRequiredService<ITraxRequestHandler>();
                var logger = scope.ServiceProvider.GetRequiredService<
                    ILogger<TraxLambdaFunction>
                >();
                var result = await handle(
                    Encoding.UTF8.GetString(body.Span),
                    handler,
                    logger,
                    ctx.RequestAborted
                );

                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(
                    JsonSerializer.Serialize(result, result.GetType(), RemoteRunJson.Write)
                );
            }
        );

    private static bool IsFromThisMachine(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress is { } caller
        && IPAddress.IsLoopback(caller.IsIPv4MappedToIPv6 ? caller.MapToIPv4() : caller);

    // recordCancelledIn: when given, a job whose token is cancelled before it starts has its
    // Pending run recorded Cancelled through this provider's data context. The Lambda entry point
    // passes it, because its token is the function's own time; the local routes do not, because
    // theirs is the request's.
    private static async Task<RemoteJobResponse> HandleExecute(
        string payloadJson,
        ITraxRequestHandler handler,
        ILogger logger,
        CancellationToken ct,
        IServiceProvider? recordCancelledIn = null
    )
    {
        var request =
            JsonSerializer.Deserialize<RemoteJobRequest>(payloadJson, CaseInsensitiveOptions)
            ?? throw new InvalidOperationException("Failed to deserialize RemoteJobRequest.");

        if (ct.IsCancellationRequested && recordCancelledIn is not null)
        {
            logger.LogWarning(
                "No time was left to run MetadataId {MetadataId}; not starting it",
                request.MetadataId
            );
            await RecordCancelledAsync(recordCancelledIn, request.MetadataId, logger);
            return new RemoteJobResponse(
                request.MetadataId,
                IsError: true,
                ErrorMessage: "The function's time ran out before the job started.",
                ExceptionType: nameof(OperationCanceledException)
            );
        }

        try
        {
            var result = await handler.ExecuteJobAsync(request, ct);
            return new RemoteJobResponse(result.MetadataId);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "HandleExecute failed for MetadataId {MetadataId}",
                request.MetadataId
            );

            // Cancelled before the job runner reached the run's row: nothing else will record it.
            // A run that did start has recorded its own outcome, and the write below matches only
            // a row still Pending.
            if (
                ex is OperationCanceledException
                && ct.IsCancellationRequested
                && recordCancelledIn is not null
            )
                await RecordCancelledAsync(recordCancelledIn, request.MetadataId, logger);

            // A TrainException's message is Trax's own account of the failure; anything else, and
            // every stack trace, stays in this function's log.
            return new RemoteJobResponse(
                request.MetadataId,
                IsError: true,
                ErrorMessage: ex is TrainException
                    ? ex.Message
                    : "The runner could not complete the request; its log has the detail.",
                ExceptionType: ex.GetType().Name
            );
        }
    }

    /// <summary>
    /// Records a run that was never started <c>Cancelled</c>, if it is still <c>Pending</c>, on
    /// <see cref="CancellationToken.None"/>: the function's own token is already cancelled. Left
    /// Pending, the row would wait for the stale-pending reaper, which records it <c>Failed</c>
    /// and so counts it toward the manifest's retries. A failure to write is logged, not thrown.
    /// </summary>
    private static async Task RecordCancelledAsync(
        IServiceProvider services,
        long metadataId,
        ILogger logger
    )
    {
        var factory = services.GetService<IDataContextProviderFactory>();
        if (factory is null)
        {
            logger.LogWarning(
                "No data provider is registered, so run {MetadataId} could not be recorded cancelled",
                metadataId
            );
            return;
        }

        try
        {
            using var context = await factory.CreateDbContextAsync(CancellationToken.None);
            var now = DateTime.UtcNow;
            var pending = context.Metadatas.Where(m =>
                m.Id == metadataId && m.TrainState == TrainState.Pending
            );

            if (context is DbContext db && db.Database.IsRelational())
                await pending.ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(m => m.TrainState, TrainState.Cancelled)
                            .SetProperty(m => m.EndTime, now),
                    CancellationToken.None
                );
            else
            {
                foreach (var run in await pending.ToListAsync(CancellationToken.None))
                {
                    run.TrainState = TrainState.Cancelled;
                    run.EndTime = now;
                }
                await context.SaveChanges(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Could not record run {MetadataId} cancelled; the stale-pending reaper will fail it",
                metadataId
            );
        }
    }

    private static async Task<RemoteRunResponse> HandleRun(
        string payloadJson,
        ITraxRequestHandler handler,
        ILogger logger,
        CancellationToken ct
    )
    {
        var request =
            JsonSerializer.Deserialize<RemoteRunRequest>(payloadJson, CaseInsensitiveOptions)
            ?? throw new InvalidOperationException("Failed to deserialize RemoteRunRequest.");

        try
        {
            return await handler.RunTrainAsync(request, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HandleRun failed for train {TrainName}", request.TrainName);
            throw;
        }
    }

    /// <summary>
    /// Resolves the <see cref="RunnerRequestVerifier"/> that <c>AddTraxJobRunner</c> registers and
    /// checks the runner has a signing posture.
    /// </summary>
    private static RunnerRequestVerifier RequireVerifier(IServiceProvider services)
    {
        var verifier =
            services.GetService<RunnerRequestVerifier>()
            ?? throw new InvalidOperationException(
                "TraxLambdaFunction requires AddTraxJobRunner(runner => ...) with a SigningKey "
                    + "or AllowUnsignedRequests()."
            );
        verifier.EnsurePosture(nameof(TraxLambdaFunction));
        return verifier;
    }

    /// <summary>
    /// Builds the service provider used by all Lambda invocations. Override only when you need
    /// full control over DI (e.g. test harnesses). The default loads <c>appsettings.json</c>
    /// plus environment variables, registers logging, calls <see cref="ConfigureServices"/>,
    /// and finalises with <c>AddTraxJobRunner()</c>, configured by <see cref="ConfigureRunner"/>.
    /// An override registers <c>AddTraxJobRunner(runner => ...)</c> itself.
    /// </summary>
    /// <remarks>
    /// Called once, lazily, on the first invocation (or the first request under
    /// <see cref="RunLocalAsync(string[], CancellationToken)"/>), and the result is reused for the life of the instance.
    /// Every invocation is refused unless the provider resolves a
    /// <see cref="RunnerRequestVerifier"/> with a signing key or with unsigned requests allowed.
    /// </remarks>
    protected virtual IServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(ConfigureLogging);
        ConfigureServices(services, configuration);
        services.AddTraxJobRunner(runner => ConfigureRunner(runner, configuration));
        return services.BuildServiceProvider();
    }
}
