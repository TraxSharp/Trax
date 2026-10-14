---
title: Run trains on AWS Lambda
description: "Build a TraxLambdaFunction, point the scheduler's UseLambdaWorkers and UseLambdaRun at it, run it locally, test it, and keep its cold starts short."
type: how-to
area: scheduling
nav_order: 30
---

# Run trains on AWS Lambda

This puts chosen trains on an AWS Lambda function: their queued runs through
`UseLambdaWorkers`, their synchronous runs through `UseLambdaRun`. The scheduler invokes the
function directly through the AWS SDK, so the function needs no URL or API Gateway. Whether Lambda
is the right place for a train is covered in [Remote Execution](/docs/scheduler/remote-execution).

## 1. Write the function

Create a class library for the function, referencing `Trax.Runner.Lambda` and the assemblies that
hold your trains, and derive from `TraxLambdaFunction`:

```csharp compile=function
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
                .SkipMigrations()
                .AddJunctionProgress())
            .AddMediator(typeof(Function).Assembly));

    protected override void ConfigureRunner(TraxJobRunnerOptions runner, IConfiguration configuration) =>
        runner.SigningKey = Convert.FromBase64String(configuration["Trax:RunnerSigningKey"]!);
}
```

`ConfigureServices` registers what the trains need and nothing else; the base class adds the job
runner. `AddJunctionProgress()` lets a cancel from the dashboard reach the train at its next junction.
`ConfigureRunner` gives the runner its posture. A signing key, at least 32 bytes, is the posture to
use; `runner.AllowUnsignedRequests()` is for a function only the scheduler's IAM role can invoke.
Without either, every invocation is refused.

Configuration comes from `appsettings.json` next to the binary and from environment variables, so
set `ConnectionStrings__TraxDatabase` and `Trax__RunnerSigningKey` on the function.

## 2. Deploy it

Deploy the function with your usual tooling, with the handler set to the `FunctionHandler` method of
your `Function` class. Give it a timeout comfortably longer than your longest train plus five
seconds: the function cancels a job five seconds (`TerminalWriteMargin`) before its own timeout so
the run can record its outcome.

Keep the payloads within Lambda's limits: an asynchronous invocation (a queued run) carries at most
256 KB of input, and a synchronous response (a run's output) at most 6 MB. A train whose input is
larger stores it elsewhere and passes a reference.

## 3. Let the scheduler invoke it

Grant the scheduler's role `lambda:InvokeFunction` on the function:

```json
{
  "Effect": "Allow",
  "Action": "lambda:InvokeFunction",
  "Resource": "arn:aws:lambda:us-east-1:123456789012:function:content-shield-runner"
}
```

Then route the trains to it, with the same signing key:

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Lambda.Extensions;

var signingKey = Convert.FromBase64String(builder.Configuration["Trax:RunnerSigningKey"]!);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(IReviewContentTrain).Assembly)
    .AddScheduler(scheduler => scheduler
        .UseLambdaWorkers(
            lambda =>
            {
                lambda.FunctionName = "content-shield-runner";
                lambda.SigningKey = signingKey;
            },
            routing => routing.ForTrain<IReviewContentTrain>())
        .UseLambdaRun(lambda =>
        {
            lambda.FunctionName = "content-shield-runner";
            lambda.SigningKey = signingKey;
        })));
```

Trains you do not route keep running on the scheduler's local workers.

## 4. Run the function locally

`RunLocalAsync` serves the function as a Kestrel server with `POST /trax/execute` and
`POST /trax/run`, running each request through the same handler as Lambda. Give the function
project a `Program.cs`:

```csharp compile=function
await new Function().RunLocalAsync(args);
```

Set the port in the function's `appsettings.json` under `Kestrel:Endpoints`. In released
Trax.Runner.Lambda versions the server reads `appsettings.json` from the current directory, so
start it from the project directory or pass `--contentRoot` with the output directory; from the next
release it reads it from the output directory.

Point a local scheduler at it with the HTTP transports and the same key, and switch to the Lambda
transports in production:

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

var signingKey = Convert.FromBase64String(builder.Configuration["Trax:RunnerSigningKey"]!);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(IReviewContentTrain).Assembly)
    .AddScheduler(scheduler => scheduler
        .UseRemoteWorkers(
            remote =>
            {
                remote.BaseUrl = "http://localhost:5399/trax/execute";
                remote.SigningKey = signingKey;
            },
            routing => routing.ForTrain<IReviewContentTrain>())
        .UseRemoteRun(remote =>
        {
            remote.BaseUrl = "http://localhost:5399/trax/run";
            remote.SigningKey = signingKey;
        })));
```

A runner with `AllowUnsignedRequests()` accepts local requests only from the loopback address, so
a scheduler in a container needs the signing key.

## 5. Test it end to end

To run the real function in a test, with no AWS, serve it with `RunLocalAsync` on a free port from
a subclass that supplies the test configuration, and point the scheduler's `UseRemoteWorkers` and
`UseRemoteRun` at that port:

```csharp compile=function
using Trax.Scheduler.Extensions;

internal sealed class TestRunner(IConfiguration configuration) : Function
{
    protected override IServiceProvider BuildServiceProvider()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        ConfigureServices(services, configuration);
        services.AddTraxJobRunner(runner => ConfigureRunner(runner, configuration));
        return services.BuildServiceProvider();
    }
}
```

```csharp fragment
var url = "http://127.0.0.1:5399";   // a free port
using var stop = new CancellationTokenSource();
var runner = new TestRunner(testConfiguration)
    .RunLocalAsync([$"--Kestrel:Endpoints:Http:Url={url}"], stop.Token);

// ... the tests ...

await stop.CancelAsync();
await runner;
```

The overload that takes a token is new in the next Trax.Runner.Lambda release; without it the
server runs until the test process ends. To test the function's own wiring without a database,
override `BuildServiceProvider` to register a fake `ITraxRequestHandler` with the runner options
and `RunnerRequestVerifier`, and call `FunctionHandler` with an envelope.

## 6. Keep cold starts short

The function builds its service provider on the first invocation of each instance and reuses it
after. To keep that first invocation fast:

- Register only what the trains need in `ConfigureServices`.
- Call `SkipMigrations()`, as above, and run migrations from the API host or CI.
- Leave out effect providers the function does not use, such as a broadcaster.

## SDK Reference

> [TraxLambdaFunction](/docs/sdk-reference/scheduler-api/trax-lambda-function) | [UseLambdaWorkers](/docs/sdk-reference/scheduler-api/use-lambda-workers) | [UseLambdaRun](/docs/sdk-reference/scheduler-api/use-lambda-run) | [UseRemoteWorkers](/docs/sdk-reference/scheduler-api/use-remote-workers) | [UseRemoteRun](/docs/sdk-reference/scheduler-api/use-remote-run)
