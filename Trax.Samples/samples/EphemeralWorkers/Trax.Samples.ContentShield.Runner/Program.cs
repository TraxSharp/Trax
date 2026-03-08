// ─────────────────────────────────────────────────────────────────────────────
// ContentShield — Ephemeral Runner (simulates Lambda/serverless execution)
//
// A minimal HTTP endpoint that receives job requests from the API via HTTP POST
// and executes trains to completion. This process has no scheduler, no polling,
// no dashboard — it only runs trains that are dispatched to it.
//
// This demonstrates the ephemeral/serverless worker pattern:
//   1. The API dispatches jobs via UseRemoteWorkers()
//   2. HttpJobSubmitter POSTs a RemoteJobRequest to this endpoint
//   3. This runner deserializes the input, runs JobRunnerTrain, and returns
//   4. No background_job table — jobs arrive directly over HTTP
//
// In production, this would be an AWS Lambda, Azure Function, or Cloud Run
// service that spins up on demand to handle each job request.
//
// Prerequisites:
//   1. Start Postgres:  cd Trax.Samples && docker compose up -d
//   2. Pack local:      ./pack-local.sh
//   3. Start runner:    dotnet run --project samples/EphemeralWorkers/Trax.Samples.ContentShield.Runner
//   4. Start API:       dotnet run --project samples/EphemeralWorkers/Trax.Samples.ContentShield.Api
//
// Endpoints:
//   POST http://localhost:5205/trax/execute  (receives RemoteJobRequest JSON — queue path)
//   POST http://localhost:5205/trax/run      (receives RemoteRunRequest JSON — synchronous run path)
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StepProvider.Progress.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

var rabbitMqConnectionString =
    builder.Configuration.GetConnectionString("RabbitMQ")
    ?? throw new InvalidOperationException("Connection string 'RabbitMQ' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Register Trax Effect + Mediator (trains, bus, discovery, execution) ──
// The runner must reference the same train assemblies as the API so it can
// resolve and execute any train type that gets dispatched.
// UseBroadcaster publishes lifecycle events to RabbitMQ so the API's
// GraphQL subscriptions are notified when queued trains complete.
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddJson()
                .SaveTrainParameters()
                .AddStepProgress()
                .UseBroadcaster(b => b.UseRabbitMq(rabbitMqConnectionString))
        )
        .AddMediator(typeof(ReviewContentTrain).Assembly)
);

// ── Register job runner endpoint ──────────────────────────────────────────
// AddTraxJobRunner() registers JobRunnerTrain and minimal supporting services.
// No scheduler, no polling, no dashboard — just the execution pipeline.
builder.Services.AddTraxJobRunner();

var app = builder.Build();

// Maps POST /trax/execute — receives RemoteJobRequest, runs JobRunnerTrain (queue path)
app.UseTraxJobRunner();

// Maps POST /trax/run — receives RemoteRunRequest, runs train synchronously and returns output
app.UseTraxRunEndpoint();

app.Run();
