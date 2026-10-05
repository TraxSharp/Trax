// ─────────────────────────────────────────────────────────────────────────────
// ContentShield: the runner, an AWS Lambda function
//
// Executes the trains the API sends it and nothing else: no scheduler, no
// polling, no dashboard. In production AWS invokes FunctionHandler with a
// LambdaEnvelope (UseLambdaWorkers / UseLambdaRun on the API). Locally,
// Program.cs calls RunLocalAsync, which serves the same work over HTTP at
// /trax/execute and /trax/run (UseRemoteWorkers / UseRemoteRun on the API).
//
//   1. The API signs each request with the shared runner key.
//   2. TraxLambdaFunction verifies the signature before reading the job.
//   3. The train runs; its metadata goes to the shared Postgres database.
//   4. Lifecycle events go to RabbitMQ, so the API's subscriptions see them.
//
// Configuration comes from appsettings.json next to the binary and from
// environment variables (ConnectionStrings__TraxDatabase, Trax__RunnerSigningKey,
// DOTNET_ENVIRONMENT). Command-line arguments reach only the local Kestrel server.
//
// Run it locally (from Trax.Samples/):
//   dotnet run --project samples/EphemeralWorkers/Trax.Samples.ContentShield.Runner
//
// Docs: https://traxsharp.net/docs/samples/content-shield
// ─────────────────────────────────────────────────────────────────────────────

using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Runner.Lambda;
using Trax.Samples.ContentShield;
using Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent;
using Trax.Scheduler.Configuration;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace Trax.Samples.ContentShield.Runner;

public class Function : TraxLambdaFunction
{
    protected override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("TraxDatabase")
            ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

        var rabbitMqConnectionString =
            configuration.GetConnectionString("RabbitMQ")
            ?? throw new InvalidOperationException("Connection string 'RabbitMQ' not found.");

        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                    effects
                        .UsePostgres(connectionString)
                        .UseBroadcaster(b =>
                            b.UseRabbitMq(
                                rabbitMqConnectionString,
                                o => o.ExchangeName = LifecycleEvents.ExchangeName
                            )
                        )
                )
                .AddMediator(typeof(ReviewContentTrain).Assembly)
        );
    }

    // A runner runs what it is sent as already authorized, so it refuses every request until it
    // knows who may send it work. Here: only a caller holding the key the API signs with.
    protected override void ConfigureRunner(
        TraxJobRunnerOptions runner,
        IConfiguration configuration
    ) => runner.SigningKey = RunnerSigningKey.Resolve(configuration, IsDevelopment(configuration));

    private static bool IsDevelopment(IConfiguration configuration) =>
        string.Equals(
            configuration["DOTNET_ENVIRONMENT"] ?? configuration["ASPNETCORE_ENVIRONMENT"],
            "Development",
            StringComparison.OrdinalIgnoreCase
        );
}
