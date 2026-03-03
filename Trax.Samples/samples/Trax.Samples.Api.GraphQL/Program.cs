// ─────────────────────────────────────────────────────────────────────────────
// Trax GraphQL API Sample
//
// Demonstrates a standalone GraphQL API powered by HotChocolate that exposes:
//   - Queries:   trains, manifests, manifestGroups, executions
//   - Mutations: queueTrain, runTrain, triggerManifest, cancelManifest, etc.
//   - Health:    GET /trax/health
//
// Prerequisites:
//   1. Start Postgres:  cd Trax.Samples && docker compose up -d
//   2. Pack local:      ./pack-local.sh
//   3. Run:             dotnet run --project Trax.Samples/samples/Trax.Samples.Api.GraphQL
//
// Try it:
//   Open http://localhost:5000/trax/graphql in a browser for Banana Cake Pop (interactive IDE)
//
//   Or via curl:
//   curl -X POST http://localhost:5000/trax/graphql \
//        -H "Content-Type: application/json" \
//        -d '{"query":"{ trains { serviceTypeName inputTypeName inputSchema { name typeName } } }"}'
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Api.GraphQL.Trains.Greet;
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

            // Schedule a recurring train so the manifests/executions queries have data
            scheduler.Schedule<IGreetTrain>(
                "greet-scheduled",
                new GreetInput { Name = "Scheduler" },
                Every.Minutes(1)
            );
        })
);

// ── Register GraphQL API ────────────────────────────────────────────────────
builder.Services.AddTraxGraphQL();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

// ── Map endpoints ───────────────────────────────────────────────────────────
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();
