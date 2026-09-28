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
    protected virtual IServiceProvider BuildServiceProvider();
    protected virtual TimeSpan TerminalWriteMargin { get; }

    public Task<object?> FunctionHandler(
        LambdaEnvelope envelope,
        ILambdaContext context
    );

    public Task RunLocalAsync(string[] args);
    internal void ConfigureRoutes(IEndpointRouteBuilder routes);
}
```

## Overridable Members

| Member | Required | Description |
|--------|----------|-------------|
| `ConfigureServices(IServiceCollection, IConfiguration)` | Yes | Register your Trax effects, mediator, data contexts, and application services. `IConfiguration` is loaded from `appsettings.json` (if present) and environment variables. Do **not** call `AddTraxJobRunner()` because the base class does this automatically. |
| `ConfigureRunner(TraxJobRunnerOptions, IConfiguration)` | In practice | Set the runner's posture: `runner.SigningKey` (shared with `UseLambdaWorkers` / `UseLambdaRun`), or `runner.AllowUnsignedRequests()` for a function only the scheduler's IAM role can invoke. The default sets nothing, so every invocation is refused. |
| `ConfigureLogging(ILoggingBuilder)` | No | Customize logging. Default: console logging at `Information` level. |
| `TerminalWriteMargin` | No | How much of `ILambdaContext.RemainingTime` is held back so a run cancelled by the function timing out can still record its outcome. Default 5 seconds. Cancellation is derived from `RemainingTime` less this margin: cancelling at `RemainingTime` itself fires at the instant Lambda freezes or kills the environment, leaving the uncancellable terminal write nowhere to happen, so the row stayed `InProgress` holding its subject until `StaleInProgressTimeout` and the reaper then recorded `Failed` rather than `Cancelled`. Widen it for a data provider with a slower write path. With less time left than the margin, the handler receives an already-cancelled token, because starting work that cannot be recorded is worse than reporting it cancelled. |
| `BuildServiceProvider()` | No | Replace the entire DI graph. The default builds `IConfiguration`, registers logging, calls `ConfigureServices`, and finishes with `AddTraxJobRunner(runner => ConfigureRunner(runner, configuration))`. An override registers `AddTraxJobRunner(runner => ...)` itself. Override only when you need full control (test harnesses are the typical case). Production code should override `ConfigureServices`, not this. |

## Envelope Dispatching

The `FunctionHandler` entry point receives a `LambdaEnvelope` directly from the AWS SDK. No API Gateway or Function URL is involved. The envelope's `Type` field determines the operation:

| Type | Handler | Description |
|------|---------|-------------|
| `Execute` | `ITraxRequestHandler.ExecuteJobAsync` | Fire-and-forget job execution (queue path). Returns `RemoteJobResponse`. |
| `Run` | `ITraxRequestHandler.RunTrainAsync` | Synchronous execution with output (run path). Returns `RemoteRunResponse`. |
| _unknown_ | _(none)_ | Throws `InvalidOperationException` |

The `LambdaEnvelope` is a shared contract defined in `Trax.Scheduler`:

```csharp
public record LambdaEnvelope(LambdaRequestType Type, string PayloadJson)
{
    public string? Signature { get; init; }
}
public enum LambdaRequestType { Execute, Run }
```

Before dispatching, the function checks its posture and, with a `SigningKey`, the envelope's `Signature` over the UTF-8 bytes of `PayloadJson`. A `Run` must also be fresh and not repeated; its nonce goes to the runner's nonce store, the Trax database by default, so concurrent instances of the function refuse a repeat too. A function with a `SigningKey` and no `UsePostgres` or `UseSqlite` calls `runner.UseInMemoryNonceStore()` in `ConfigureRunner`, or registers an `INonceStore`, or it refuses to start. An `Execute` is checked for its signature only, because Lambda retries an asynchronous invocation with the same payload; the job's `Pending` metadata row stops a second run. A refused envelope throws, so the invocation fails and Lambda's retry and dead-letter settings apply.

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

    // The same key as UseLambdaWorkers / UseLambdaRun on the scheduler
    protected override void ConfigureRunner(TraxJobRunnerOptions runner, IConfiguration configuration) =>
        runner.SigningKey = Convert.FromBase64String(configuration["Trax:RunnerSigningKey"]!);
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

Use `RunLocalAsync` to run the Lambda function as a local Kestrel web server. This maps `POST /trax/execute` and `POST /trax/run` endpoints, which enforce the same posture (a signed request carries the `Trax-Signature` header and must be fresh; a refused one gets `401`), and which wrap incoming HTTP request bodies into `LambdaEnvelope` payloads and execute them through the same handler logic as the Lambda entry point.

```csharp
// Program.cs
await new Function().RunLocalAsync(args);
```

This enables a smooth development workflow:
- **Local dev:** Scheduler uses `UseRemoteWorkers()` + `UseRemoteRun()` to hit the local Kestrel server
- **Production:** Scheduler uses `UseLambdaWorkers()` + `UseLambdaRun()` for direct SDK invocation

The local server reads its port from `appsettings.json` (via Kestrel configuration) and exposes the same endpoints that the Lambda would handle in production.

Internally `RunLocalAsync` delegates the route mapping to an internal `ConfigureRoutes(IEndpointRouteBuilder)` method. Tests can host the Lambda's HTTP surface against `Microsoft.AspNetCore.TestHost` by calling `ConfigureRoutes` directly, without spinning up a real Kestrel listener.

## Testing

Two extension points exist specifically for tests:

1. Override `BuildServiceProvider` to swap in a fake `ITraxRequestHandler` (or any other dependency) without exercising `AddTraxJobRunner` and the full effect/mediator stack.
2. Call `ConfigureRoutes` from a `TestServer`-hosted pipeline to exercise the `/trax/execute` and `/trax/run` endpoints in-process. `ConfigureRoutes` is `internal`, made visible to the Trax test assemblies via `InternalsVisibleTo`.

```csharp
// Unit test: stub the request handler.
private sealed class FakeFunction : TraxLambdaFunction
{
    public ITraxRequestHandler Handler { get; } = new RecordingHandler();

    protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }

    protected override IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Handler);
        services.AddSingleton(new TraxJobRunnerOptions().AllowUnsignedRequests());
        services.AddSingleton<RunnerRequestVerifier>();
        return services.BuildServiceProvider();
    }
}
```

## Configuration

The base class automatically builds an `IConfiguration` from:

1. `appsettings.json` (optional, loaded from `AppContext.BaseDirectory`)
2. Environment variables

This means you can use `appsettings.json` for local development and environment variables in Lambda. Both work out of the box. The configuration is passed to `ConfigureServices` and registered in DI as `IConfiguration`.

## Cold Start Optimization

The service provider is built **lazily on the first invocation**, not during Lambda container creation. Subsequent invocations within the same container reuse the same provider.

To minimize cold start time:

- Keep `ConfigureServices` lean. Only register what the runner needs.
- Use `SkipMigrations()`. Migrations should run from the API or CI, not the Lambda.
- Avoid unnecessary effect providers. If the runner doesn't need broadcasting, don't register it.

## How It Works

1. Lambda runtime creates an instance of your `Function` class
2. On the first `FunctionHandler` invocation, `BuildServiceProvider()` is called:
   - Builds `IConfiguration` from `appsettings.json` + environment variables
   - Creates a `ServiceCollection`
   - Registers `IConfiguration` as a singleton
   - Calls `ConfigureLogging()` (virtual, overridable)
   - Calls `ConfigureServices()` (your code)
   - Calls `AddTraxJobRunner(runner => ConfigureRunner(runner, configuration))` (automatic)
   - Builds and caches the `IServiceProvider`
   - The whole method is `protected virtual`, so test harnesses can replace it wholesale.
3. Each invocation creates a new DI scope and resolves `ITraxRequestHandler`
4. Cancellation is derived from `ILambdaContext.RemainingTime`
5. The `LambdaEnvelope.Type` field determines which handler method is called

## Error Handling

For `Execute` requests, exceptions are logged and returned as a `RemoteJobResponse` with structured error fields (`IsError`, `ErrorMessage`, `ExceptionType`). `ErrorMessage` is the message of a `TrainException` or fixed text, and no stack trace is returned. Errors that occur within the train itself are also persisted to the `Metadata` table by `ServiceTrain.Run`. However, pre-train errors (e.g., deserialization failures) only appear in the log output. The payload is read case-insensitively, and one that repeats a property (in the same or a different case) is refused with a `JsonException`, before any handler runs. The `LambdaJobSubmitter` on the scheduler side does not read the response (fire-and-forget).

For `Run` requests, exceptions are logged before being rethrown. `ITraxRequestHandler.RunTrainAsync` returns a `RemoteRunResponse` that may contain structured error fields. The `LambdaRunExecutor` on the scheduler side reads the response and reconstructs a `TrainException` with the full error context.

## See Also

- [Remote Execution](/docs/scheduler/remote-execution): architecture overview and deployment models
- [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers): scheduler-side configuration for Lambda dispatch
- [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run): scheduler-side configuration for Lambda run execution
- [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner): what `AddTraxJobRunner()` registers (called automatically by the base class)
