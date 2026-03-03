// ─────────────────────────────────────────────────────────────────────────────
// Trax REST API Sample
//
// Demonstrates a standalone REST API that exposes:
//   - Train discovery   GET  /trax/api/trains
//   - Queue a train     POST /trax/api/trains/queue
//   - Run a train       POST /trax/api/trains/run
//   - Scheduler ops     POST /trax/api/scheduler/trigger/{externalId}, etc.
//   - Read-only queries GET  /trax/api/manifests, /trax/api/executions, etc.
//   - Health check      GET  /trax/health
//
// Prerequisites:
//   1. Start Postgres:  cd Trax.Samples && docker compose up -d
//   2. Pack local:      ./pack-local.sh
//   3. Run:             dotnet run --project Trax.Samples/samples/Trax.Samples.Api.Rest
//
// Try it:
//   curl http://localhost:5000/trax/api/trains
//   curl -X POST http://localhost:5000/trax/api/trains/queue \
//        -H "Content-Type: application/json" \
//        -d '{"trainName":"Trax.Samples.Api.Rest.Trains.Greet.IGreetTrain","input":{"name":"Alice"}}'
//   curl http://localhost:5000/trax/health
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Extensions;
using Trax.Api.Rest.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Api.Rest.Trains.Greet;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Register Trax Effect + Mediator (trains, bus, discovery, execution) ─────
builder.Services.AddTraxEffects(options =>
    options
        .AddServiceTrainBus(assemblies: [typeof(Program).Assembly])
        .AddPostgresEffect(connectionString)
        .AddEffectDataContextLogging()
        .AddJsonEffect()
        .SaveTrainParameters()
        .AddScheduler(scheduler =>
        {
            scheduler.JobDispatcherPollingInterval(TimeSpan.FromSeconds(2)).UsePostgresTaskServer();

            // Schedule a recurring train so the manifests/executions endpoints have data
            scheduler.Schedule<IGreetTrain>(
                "greet-scheduled",
                new GreetInput { Name = "Scheduler" },
                Every.Minutes(1)
            );
        })
);

// ── Register REST API ───────────────────────────────────────────────────────
builder.Services.AddTraxRestApi();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

// ── Map endpoints ───────────────────────────────────────────────────────────
app.UseTraxRestApi();
app.MapHealthChecks("/trax/health");

app.Run();
