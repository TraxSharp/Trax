using System.Text.Json;
using Amazon.Lambda.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Lambda;
using Trax.Scheduler.Services.RequestHandler;
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
/// <b>Local development:</b> Use <see cref="RunLocalAsync"/> to run the function as a
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
    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Lazy<IServiceProvider> _serviceProvider;

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
    /// Override to customize logging. Default adds console logging at <see cref="LogLevel.Information"/>.
    /// </summary>
    /// <param name="logging">The logging builder to configure</param>
    protected virtual void ConfigureLogging(ILoggingBuilder logging)
    {
        logging.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
    }

    /// <summary>
    /// Lambda entry point for direct SDK invocation.
    /// Receives a <see cref="LambdaEnvelope"/> and dispatches to the appropriate handler
    /// based on <see cref="LambdaEnvelope.Type"/>.
    /// Cancellation is derived from <see cref="ILambdaContext.RemainingTime"/>.
    /// </summary>
    public async Task<object?> FunctionHandler(LambdaEnvelope envelope, ILambdaContext context)
    {
        using var cts = new CancellationTokenSource(context.RemainingTime);
        using var scope = _serviceProvider.Value.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ITraxRequestHandler>();

        return envelope.Type switch
        {
            LambdaRequestType.Execute => await HandleExecute(
                envelope.PayloadJson,
                handler,
                cts.Token
            ),
            LambdaRequestType.Run => await HandleRun(envelope.PayloadJson, handler, cts.Token),
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
    /// <example>
    /// <code>
    /// // Program.cs
    /// await new Function().RunLocalAsync(args);
    /// </code>
    /// </example>
    /// <param name="args">Command-line arguments passed to <see cref="WebApplication.CreateBuilder"/></param>
    public async Task RunLocalAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        app.MapPost(
            "/trax/execute",
            async (HttpContext ctx) =>
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();

                using var scope = _serviceProvider.Value.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<ITraxRequestHandler>();
                var result = await HandleExecute(body, handler, ctx.RequestAborted);

                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(result));
            }
        );

        app.MapPost(
            "/trax/run",
            async (HttpContext ctx) =>
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();

                using var scope = _serviceProvider.Value.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<ITraxRequestHandler>();
                var result = await HandleRun(body, handler, ctx.RequestAborted);

                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(result));
            }
        );

        await app.RunAsync();
    }

    private static async Task<RemoteJobResponse> HandleExecute(
        string payloadJson,
        ITraxRequestHandler handler,
        CancellationToken ct
    )
    {
        var request =
            JsonSerializer.Deserialize<RemoteJobRequest>(payloadJson, CaseInsensitiveOptions)
            ?? throw new InvalidOperationException("Failed to deserialize RemoteJobRequest.");

        try
        {
            var result = await handler.ExecuteJobAsync(request, ct);
            return new RemoteJobResponse(result.MetadataId);
        }
        catch (Exception ex)
        {
            return new RemoteJobResponse(
                request.MetadataId,
                IsError: true,
                ErrorMessage: ex.Message,
                ExceptionType: ex.GetType().Name,
                StackTrace: ex.StackTrace
            );
        }
    }

    private static async Task<RemoteRunResponse> HandleRun(
        string payloadJson,
        ITraxRequestHandler handler,
        CancellationToken ct
    )
    {
        var request =
            JsonSerializer.Deserialize<RemoteRunRequest>(payloadJson, CaseInsensitiveOptions)
            ?? throw new InvalidOperationException("Failed to deserialize RemoteRunRequest.");

        return await handler.RunTrainAsync(request, ct);
    }

    private IServiceProvider BuildServiceProvider()
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
        services.AddTraxJobRunner();
        return services.BuildServiceProvider();
    }
}
