using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Runner.Lambda;

/// <summary>
/// Delegate for Lambda route handlers.
/// </summary>
/// <param name="body">The raw request body</param>
/// <param name="services">The scoped service provider for this invocation</param>
/// <param name="ct">Cancellation token derived from Lambda's remaining time</param>
/// <returns>The API Gateway response</returns>
public delegate Task<APIGatewayHttpApiV2ProxyResponse> LambdaRouteHandler(
    string body,
    IServiceProvider services,
    CancellationToken ct
);

/// <summary>
/// Base class for AWS Lambda functions that execute Trax trains via API Gateway HTTP API v2.
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
/// By default, routes <c>/trax/execute</c> and <c>/trax/run</c> are registered.
/// Override <see cref="ConfigureRoutes"/> to add custom routes or replace the defaults.
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
    private readonly Dictionary<string, LambdaRouteHandler> _routes = new(
        StringComparer.OrdinalIgnoreCase
    );

    protected TraxLambdaFunction()
    {
        _serviceProvider = new Lazy<IServiceProvider>(BuildServiceProvider);
        ConfigureRoutes();
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
    /// Override to add custom routes or replace the defaults. Called once during construction.
    /// The default implementation registers <c>/trax/execute</c> and <c>/trax/run</c>.
    /// </summary>
    protected virtual void ConfigureRoutes()
    {
        MapRoute("/trax/execute", HandleExecute);
        MapRoute("/trax/run", HandleRun);
    }

    /// <summary>
    /// Registers a route handler for the given path.
    /// </summary>
    /// <param name="path">The URL path to match (e.g., "/trax/execute")</param>
    /// <param name="handler">The handler to invoke when the path matches</param>
    protected void MapRoute(string path, LambdaRouteHandler handler)
    {
        _routes[path.TrimEnd('/')] = handler;
    }

    /// <summary>
    /// Lambda entry point for API Gateway HTTP API v2 (payload format 2.0).
    /// Cancellation is derived from <see cref="ILambdaContext.RemainingTime"/>.
    /// </summary>
    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    )
    {
        using var cts = new CancellationTokenSource(context.RemainingTime);
        using var scope = _serviceProvider.Value.CreateScope();
        var sp = scope.ServiceProvider;

        var path = request.RawPath?.TrimEnd('/') ?? "";

        if (_routes.TryGetValue(path, out var handler))
            return await handler(request.Body, sp, cts.Token);

        return JsonResponse(404, new { error = $"Unknown route: {path}" });
    }

    /// <summary>
    /// Runs the Lambda function as a local Kestrel web server for development.
    /// Maps all registered routes as POST endpoints. Use this in a <c>Program.cs</c>
    /// entry point for local testing without AWS tooling.
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

        foreach (var (path, handler) in _routes)
        {
            app.MapPost(
                path,
                async (HttpContext ctx) =>
                {
                    using var reader = new StreamReader(ctx.Request.Body);
                    var body = await reader.ReadToEndAsync();

                    using var scope = _serviceProvider.Value.CreateScope();
                    var result = await handler(body, scope.ServiceProvider, ctx.RequestAborted);

                    ctx.Response.StatusCode = result.StatusCode;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(result.Body);
                }
            );
        }

        await app.RunAsync();
    }

    /// <summary>
    /// Creates a JSON response for the given status code and body.
    /// </summary>
    protected static APIGatewayHttpApiV2ProxyResponse JsonResponse(int statusCode, object body) =>
        new()
        {
            StatusCode = statusCode,
            Body = JsonSerializer.Serialize(body),
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        };

    private static async Task<APIGatewayHttpApiV2ProxyResponse> HandleExecute(
        string body,
        IServiceProvider sp,
        CancellationToken ct
    )
    {
        try
        {
            var request =
                JsonSerializer.Deserialize<RemoteJobRequest>(body, CaseInsensitiveOptions)
                ?? throw new InvalidOperationException("Failed to deserialize RemoteJobRequest.");

            var handler = sp.GetRequiredService<ITraxRequestHandler>();
            var result = await handler.ExecuteJobAsync(request, ct);
            return JsonResponse(200, new { metadataId = result.MetadataId });
        }
        catch (Exception ex)
        {
            return JsonResponse(500, new { error = ex.Message });
        }
    }

    private static async Task<APIGatewayHttpApiV2ProxyResponse> HandleRun(
        string body,
        IServiceProvider sp,
        CancellationToken ct
    )
    {
        var request =
            JsonSerializer.Deserialize<RemoteRunRequest>(body, CaseInsensitiveOptions)
            ?? throw new InvalidOperationException("Failed to deserialize RemoteRunRequest.");

        var handler = sp.GetRequiredService<ITraxRequestHandler>();
        var response = await handler.RunTrainAsync(request, ct);
        return JsonResponse(200, response);
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
