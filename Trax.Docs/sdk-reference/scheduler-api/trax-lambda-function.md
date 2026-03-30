---
layout: default
title: TraxLambdaFunction
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 11
---

# TraxLambdaFunction

Abstract base class for AWS Lambda functions that execute Trax trains via direct SDK invocation. Handles service provider lifecycle, envelope-based dispatching, cancellation, and error handling so your Lambda function is just a DI configuration.

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

    public Task<object?> FunctionHandler(
        LambdaEnvelope envelope,
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

## Envelope Dispatching

The `FunctionHandler` entry point receives a `LambdaEnvelope` directly from the AWS SDK — no API Gateway or Function URL is involved. The envelope's `Type` field determines the operation:

| Type | Handler | Description |
|------|---------|-------------|
| `Execute` | `ITraxRequestHandler.ExecuteJobAsync` | Fire-and-forget job execution (queue path). Returns `RemoteJobResponse`. |
| `Run` | `ITraxRequestHandler.RunTrainAsync` | Synchronous execution with output (run path). Returns `RemoteRunResponse`. |
| _unknown_ | — | Throws `InvalidOperationException` |

The `LambdaEnvelope` is a shared contract defined in `Trax.Scheduler`:

```csharp
public record LambdaEnvelope(LambdaRequestType Type, string PayloadJson);
public enum LambdaRequestType { Execute, Run }
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

Use `RunLocalAsync` to run the Lambda function as a local Kestrel web server. This maps `POST /trax/execute` and `POST /trax/run` endpoints that wrap incoming HTTP request bodies into `LambdaEnvelope` payloads and execute them through the same handler logic as the Lambda entry point.

```csharp
// Program.cs
await new Function().RunLocalAsync(args);
```

This enables a smooth development workflow:
- **Local dev:** Scheduler uses `UseRemoteWorkers()` + `UseRemoteRun()` to hit the local Kestrel server
- **Production:** Scheduler uses `UseLambdaWorkers()` + `UseLambdaRun()` for direct SDK invocation

The local server reads its port from `appsettings.json` (via Kestrel configuration) and exposes the same endpoints that the Lambda would handle in production.

## Configuration

The base class automatically builds an `IConfiguration` from:

1. `appsettings.json` (optional — loaded from `AppContext.BaseDirectory`)
2. Environment variables

This means you can use `appsettings.json` for local development and environment variables in Lambda — both work seamlessly. The configuration is passed to `ConfigureServices` and registered in DI as `IConfiguration`.

## Cold Start Optimization

The service provider is built **lazily on the first invocation**, not during Lambda container creation. Subsequent invocations within the same container reuse the same provider.

To minimize cold start time:

- Keep `ConfigureServices` lean — only register what the runner needs
- Use `SkipMigrations()` — migrations should run from the API or CI, not the Lambda
- Avoid unnecessary effect providers — if the runner doesn't need broadcasting, don't register it

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
5. The `LambdaEnvelope.Type` field determines which handler method is called

## Error Handling

For `Execute` requests, exceptions are logged and returned as a `RemoteJobResponse` with structured error fields (`IsError`, `ErrorMessage`, `ExceptionType`, `StackTrace`). Errors that occur within the train itself are also persisted to the `Metadata` table by `ServiceTrain.Run`. However, pre-train errors (e.g., deserialization failures) only appear in the log output. The `LambdaJobSubmitter` on the scheduler side does not read the response (fire-and-forget).

For `Run` requests, exceptions are logged before being rethrown. `ITraxRequestHandler.RunTrainAsync` returns a `RemoteRunResponse` that may contain structured error fields. The `LambdaRunExecutor` on the scheduler side reads the response and reconstructs a `TrainException` with the full error context.

## See Also

- [Remote Execution](/docs/scheduler/remote-execution) — architecture overview and deployment models
- [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers) — scheduler-side configuration for Lambda dispatch
- [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run) — scheduler-side configuration for Lambda run execution
- [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner) — what `AddTraxJobRunner()` registers (called automatically by the base class)
