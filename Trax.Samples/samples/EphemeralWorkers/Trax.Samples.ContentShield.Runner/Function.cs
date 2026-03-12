// ─────────────────────────────────────────────────────────────────────────────
// ContentShield — AWS Lambda Runner (API Gateway HTTP API v2)
//
// An AWS Lambda function that receives job requests from the API via
// API Gateway and executes trains to completion. This process has no
// scheduler, no polling, no dashboard — it only runs trains dispatched to it.
//
// How it works:
//   1. The API dispatches jobs via UseRemoteWorkers() or UseRemoteRun()
//   2. API Gateway routes /trax/execute and /trax/run to this Lambda
//   3. TraxLambdaFunction handles deserialization, execution, and response
//   4. Results are persisted to the shared Postgres database
//   5. Lifecycle events are broadcast via RabbitMQ so the API can push
//      real-time GraphQL subscription updates
//
// Local development:
//   dotnet run --project samples/EphemeralWorkers/Trax.Samples.ContentShield.Runner
//   (uses Program.cs to run as a local Kestrel web server)
//
// API configuration (sends to this Runner via HTTP):
//   scheduler.UseRemoteWorkers(remote =>
//       remote.BaseUrl = "http://localhost:5205/trax/execute"
//   );
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
using Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent;

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
                        .UseBroadcaster(b => b.UseRabbitMq(rabbitMqConnectionString))
                )
                .AddMediator(typeof(ReviewContentTrain).Assembly)
        );
    }
}
