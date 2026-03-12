// ─────────────────────────────────────────────────────────────────────────────
// Trax GraphQL API
//
// A GraphQL API powered by HotChocolate. Handles lightweight operations
// directly via mutations and can queue heavy work for a separate scheduler
// process by passing mode: QUEUE.
//
// Prerequisites:
//   1. Start PostgreSQL (e.g. docker compose up -d)
//   2. Run this project: dotnet run
//
// Try it:
//   Open http://localhost:5002/trax/graphql in a browser for Banana Cake Pop IDE
//
//   # Query a train directly (typed query from [TraxQuery])
//   query { discover { lookup(input: { id: "42" }) { id name createdAt } } }
//
//   # Run a mutation (from [TraxMutation])
//   mutation { dispatch { helloWorld(input: { name: "Trax" }) { externalId metadataId } } }
//
//   # Health check
//   curl http://localhost:5002/trax/health
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Register Trax Effect + Mediator ─────────────────────────────────────
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects.UsePostgres(connectionString).AddJson().SaveTrainParameters()
        )
        .AddMediator(typeof(Program).Assembly)
);

// ── Register GraphQL API ────────────────────────────────────────────────
builder.Services.AddTraxGraphQL();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

// ── Map endpoints ───────────────────────────────────────────────────────
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();
