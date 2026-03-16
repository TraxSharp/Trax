---
layout: default
title: TraxLambdaFunction
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 11
---

# TraxLambdaFunction

Abstract base class for AWS Lambda functions that execute Trax trains via API Gateway HTTP API v2. Handles service provider lifecycle, configuration loading, request routing, cancellation, and error handling so your Lambda function is just a DI configuration.

## Package

```
dotnet add package Trax.Runner.Lambda
```

## Signature

```csharp
public abstract class TraxLambdaFunction
{
    protected abstract void ConfigureServices(IServiceCollection services, IConfiguration configuration);
    protected virtual void ConfigureLogging(ILoggingBuilder logging);
    protected virtual void ConfigureRoutes();
    protected void MapRoute(string path, LambdaRouteHandler handler);

    public Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context
    );

    public Task RunLocalAsync(string[] args);
}
```

## Overridable Members

| Member | Required | Description |
|--------|----------|-------------|
| `ConfigureServices(IServiceCollection, IConfiguration)` | Yes | Register your Trax effects, mediator, data contexts, and application services. `IConfiguration` is loaded from `appsettings.json` (if present) and environment variables. Do **not** call `AddTraxJobRunner()` — the base class does this automatically. |
| `ConfigureLogging(ILoggingBuilder)` | No | Customize logging. Default: console logging at `Information` level. |
| `ConfigureRoutes()` | No | Customize route registration. Default registers `/trax/execute` and `/trax/run`. Call `MapRoute()` to add or replace routes. |

## Routes

The `FunctionHandler` entry point routes based on `request.RawPath`:

| Route | Handler | Description |
|-------|---------|-------------|
| `/trax/execute` | `ITraxRequestHandler.ExecuteJobAsync` | Fire-and-forget job execution (queue path) |
| `/trax/run` | `ITraxRequestHandler.RunTrainAsync` | Synchronous execution with output (run path) |
| _anything else_ | — | Returns 404 |

Override `ConfigureRoutes()` to add custom routes:

```csharp
protected override void ConfigureRoutes()
{
    base.ConfigureRoutes(); // keep default routes
    MapRoute("/my/custom-route", async (body, services, ct) =>
    {
        // resolve services, handle request
        return JsonResponse(200, new { ok = true });
    });
}
```

## Examples

### Minimal Runner

```csharp
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Trax.Runner.Lambda;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

public class Function : TraxLambdaFunction
{
    protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connString = configuration.GetConnectionString("TraxDatabase")!;

        services.AddTrax(trax => trax
            .AddEffects(effects => effects.UsePostgres(connString))
            .AddMediator(typeof(MyTrain).Assembly));
    }
}
```

### With Custom Logging and Effects

```csharp
[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

public class Function : TraxLambdaFunction
{
    protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connString = configuration.GetConnectionString("TraxDatabase")!;
        var rabbitMq = configuration.GetConnectionString("RabbitMQ")!;

        services.AddMyDataContexts(connString);

        services.AddTrax(trax => trax
            .AddEffects(effects => effects
                .UsePostgres(connString)
                .SaveTrainParameters()
                .AddJunctionProgress()
                .UseBroadcaster(b => b.UseRabbitMq(rabbitMq)))
            .AddMediator(
                typeof(MyClientTrains.AssemblyMarker).Assembly,
                typeof(MyAdminTrains.AssemblyMarker).Assembly));
    }

    protected override void ConfigureLogging(ILoggingBuilder logging)
    {
        logging.AddConsole().SetMinimumLevel(LogLevel.Debug);
    }
}
```

## Local Development

Use `RunLocalAsync` to run the Lambda function as a local Kestrel web server. This maps all registered routes as POST endpoints, so the API can dispatch jobs to it via HTTP just like it would to the deployed Lambda.

```csharp
// Program.cs
await new Function().RunLocalAsync(args);
```

The local server reads its port from `appsettings.json` (via Kestrel configuration) and exposes the same `/trax/execute` and `/trax/run` endpoints that API Gateway would route to in production.

## Configuration

The base class automatically builds an `IConfiguration` from:

1. `appsettings.json` (optional — loaded from `AppContext.BaseDirectory`)
2. Environment variables

This means you can use `appsettings.json` for local development and environment variables in Lambda — both work seamlessly. The configuration is passed to `ConfigureServices` and registered in DI as `IConfiguration`.

## Cold Start Optimization

The service provider is built **lazily on the first invocation**, not during Lambda container creation. Subsequent invocations within the same container reuse the same provider.

To minimize cold start time:

- Keep `ConfigureServices` lean — only register what the runner needs
- Avoid unnecessary effect providers — if the runner doesn't need broadcasting, don't register it
- Consider splitting API Gateway routes across separate Lambda functions if different routes pull in different dependency trees

## How It Works

1. Lambda runtime creates an instance of your `Function` class
2. On the first `FunctionHandler` invocation, `BuildServiceProvider()` is called:
   - Builds `IConfiguration` from `appsettings.json` + environment variables
   - Creates a `ServiceCollection`
   - Registers `IConfiguration` as a singleton
   - Calls `ConfigureLogging()` (virtual, overridable)
   - Calls `ConfigureServices()` (your code)
   - Calls `AddTraxJobRunner()` (automatic)
   - Builds and caches the `IServiceProvider`
3. Each invocation creates a new DI scope and resolves `ITraxRequestHandler`
4. Cancellation is derived from `ILambdaContext.RemainingTime`
5. Routes are matched from the route table (populated by `ConfigureRoutes()`)

## See Also

- [Remote Execution]({{ site.baseurl }}{% link scheduler/remote-execution.md %}) — architecture overview and deployment models
- [AddTraxJobRunner]({{ site.baseurl }}{% link sdk-reference/scheduler-api/add-trax-job-runner.md %}) — what `AddTraxJobRunner()` registers (called automatically by the base class)
- [UseSqsWorkers]({{ site.baseurl }}{% link sdk-reference/scheduler-api/use-sqs-workers.md %}) — alternative: SQS-triggered Lambda (uses `SqsJobRunnerHandler` instead)
