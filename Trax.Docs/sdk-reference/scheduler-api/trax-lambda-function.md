---
title: TraxLambdaFunction
description: Reference for TraxLambdaFunction, the base class for AWS Lambda functions that run Trax trains from a LambdaEnvelope, and its RunLocalAsync server.
type: reference
area: scheduling
parent: Scheduler API
grand_parent: SDK Reference
nav_order: 17
package: Trax.Runner.Lambda
namespace: Trax.Runner.Lambda
source:
  - Trax.Scheduler/src/Trax.Runner.Lambda/TraxLambdaFunction.cs
---

# TraxLambdaFunction

The abstract base class for an AWS Lambda function that runs Trax trains sent by
[UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers) and
[UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run). It builds and keeps the service
provider, dispatches each `LambdaEnvelope`, derives cancellation from the time the function has
left, and serves the same routes over HTTP for local runs.

## Signature

```csharp fragment
public abstract class TraxLambdaFunction
{
    protected abstract void ConfigureServices(IServiceCollection services, IConfiguration configuration);
    protected virtual void ConfigureRunner(TraxJobRunnerOptions runner, IConfiguration configuration);
    protected virtual void ConfigureLogging(ILoggingBuilder logging);
    protected virtual IServiceProvider BuildServiceProvider();
    protected virtual TimeSpan TerminalWriteMargin { get; }

    public Task<object?> FunctionHandler(LambdaEnvelope envelope, ILambdaContext context);

    public Task RunLocalAsync(string[] args);
    public Task RunLocalAsync(string[] args, CancellationToken cancellationToken);
}
```

## Members

| Member | Description |
|--------|-------------|
| `ConfigureServices(IServiceCollection, IConfiguration)` | Abstract. Registers the function's Trax effects, mediator, data contexts and application services. The base class calls `AddTraxJobRunner()` itself, so this does not |
| `ConfigureRunner(TraxJobRunnerOptions, IConfiguration)` | Sets the runner's posture: `runner.SigningKey` (shared with `UseLambdaWorkers` / `UseLambdaRun`), or `runner.AllowUnsignedRequests()`. The default sets nothing, so every invocation is refused |
| `ConfigureLogging(ILoggingBuilder)` | Configures logging. Default: console at `Information` |
| `TerminalWriteMargin` | How much of `ILambdaContext.RemainingTime` is held back so a run cancelled by the function's timeout can still record its outcome. Default 5 seconds; at most half the time left is held back |
| `BuildServiceProvider()` | Builds the whole service graph. The default builds `IConfiguration`, registers it and logging, calls `ConfigureLogging` and `ConfigureServices`, and finishes with `AddTraxJobRunner(runner => ConfigureRunner(runner, configuration))`. An override registers `AddTraxJobRunner` itself |
| `FunctionHandler(LambdaEnvelope, ILambdaContext)` | The Lambda entry point. Dispatches the envelope by its `Type` and returns the handler's response |
| `RunLocalAsync(string[])` | Serves the function as a local Kestrel server until it stops on Ctrl+C or process exit |
| `RunLocalAsync(string[], CancellationToken)` | The same, also stopping when the token is cancelled; the task completes once the server has shut down. New in the next Trax.Runner.Lambda release |

## Behavior

### Service provider

The provider is built on the first `FunctionHandler` call, not when Lambda creates the instance,
and reused for every later call on that instance. Each call creates its own DI scope and resolves
`ITraxRequestHandler` from it.

### Configuration

`IConfiguration` is built from `appsettings.json` (optional, read from `AppContext.BaseDirectory`)
and environment variables, passed to `ConfigureServices` and `ConfigureRunner`, and registered as
`IConfiguration`. Command-line arguments are not part of it, even under `RunLocalAsync`, where they
reach only the Kestrel server. There is no `IHostEnvironment`; a function reads
`DOTNET_ENVIRONMENT` (or `ASPNETCORE_ENVIRONMENT`) from the configuration.

### Envelope dispatching

| `Type` | Handler | Returns |
|--------|---------|---------|
| `Execute` | `ITraxRequestHandler.ExecuteJobAsync` | `RemoteJobResponse` |
| `Run` | `ITraxRequestHandler.RunTrainAsync` | `RemoteRunResponse` |
| anything else | none | throws `InvalidOperationException` |

`LambdaEnvelope` and `LambdaRequestType` are defined in Trax.Scheduler:
`public record LambdaEnvelope(LambdaRequestType Type, string PayloadJson)` with an init-only
`string? Signature`, and `public enum LambdaRequestType { Execute, Run }`.

Before dispatching, the function checks its posture and, with a `SigningKey`, the envelope's
`Signature` over the UTF-8 bytes of `PayloadJson`. A `Run` must also be fresh and not repeated; its
nonce goes to the runner's nonce store, the Trax database by default, so concurrent instances
refuse a repeat too. A function with a `SigningKey` and no `UsePostgres` or `UseSqlite` calls
`runner.UseInMemoryNonceStore()` in `ConfigureRunner`, or registers an `INonceStore`, or it refuses
to start. An `Execute` is checked for its signature only, because Lambda retries an asynchronous
invocation with the same payload; the job's `Pending` metadata row stops a second run. A refused
envelope throws, so the invocation fails and Lambda's retry and dead-letter settings apply.

### Timeouts

Cancellation is derived from `RemainingTime` less the margin actually used, the smaller of
`TerminalWriteMargin` and `RemainingTime / 2`. A function whose timeout is at or below the margin
(AWS's default is three seconds) still runs its jobs, and the first time an instance has no more
time left than the margin it logs a warning. An `Execute` whose time runs out before its job starts
does not start it: the run is recorded `Cancelled` (only while it is still `Pending`, written on
`CancellationToken.None`) rather than left for the stale-pending reaper to fail.

### Errors

For `Execute`, exceptions are logged and returned as a `RemoteJobResponse` with `IsError`,
`ErrorMessage` and `ExceptionType`. `ErrorMessage` is a `TrainException`'s message or fixed text,
and no stack trace is returned. Errors inside the train are also persisted to the `Metadata` table
by `ServiceTrain.Run`; errors before the train starts (such as a payload that does not
deserialize) appear only in the log. The payload is read case-insensitively, and one that repeats a
property is refused with a `JsonException` before any handler runs. The Lambda job submitter does
not read the response.

For `Run`, exceptions are logged and rethrown. `RunTrainAsync` returns a `RemoteRunResponse` that
may carry error fields, from which the Lambda run executor rebuilds a `TrainException`.

### Local server

`RunLocalAsync` builds a `WebApplication` from `args`, maps `POST /trax/execute` and
`POST /trax/run`, wraps each request body in a `LambdaEnvelope`, and runs it through the same
handler as `FunctionHandler`. The server reads `appsettings.json`, its Kestrel endpoints included,
from its content root: from the next Trax.Runner.Lambda release `AppContext.BaseDirectory`, and in
released versions the current directory. A `--contentRoot` argument, or `DOTNET_CONTENTROOT` /
`ASPNETCORE_CONTENTROOT`, sets it.

| Runner posture | What the local routes accept |
|----------------|------------------------------|
| `SigningKey` | Requests with a valid, fresh `Trax-Signature` header, from any address. The header is checked before the body is read (`401`), and a body over `MaxRequestBodyBytes` gets `413` |
| `AllowUnsignedRequests()` | Unsigned requests from this machine's loopback address only, whatever address the server listens on; any other caller gets `401` |

The routes are mapped by an internal `ConfigureRoutes(IEndpointRouteBuilder)`, visible to the Trax
test assemblies only.

## Example

```csharp compile
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Mediator.Extensions;
using Trax.Runner.Lambda;
using Trax.Scheduler.Configuration;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

public class Function : TraxLambdaFunction
{
    protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddTrax(trax => trax
            .AddEffects(effects => effects
                .UsePostgres(configuration.GetConnectionString("TraxDatabase")!)
                .AddJunctionProgress())
            .AddMediator(typeof(Function).Assembly));

    // The same key as UseLambdaWorkers and UseLambdaRun on the scheduler.
    protected override void ConfigureRunner(TraxJobRunnerOptions runner, IConfiguration configuration) =>
        runner.SigningKey = Convert.FromBase64String(configuration["Trax:RunnerSigningKey"]!);
}
```

## See also

- [Run trains on AWS Lambda](/docs/scheduler/run-trains-on-lambda), building, running locally and testing a function
- [How remote dispatch works](/docs/scheduler/how-remote-dispatch-works)
- [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers) and [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run), the scheduler side
- [AddTraxJobRunner](/docs/sdk-reference/scheduler-api/add-trax-job-runner), what the base class registers
