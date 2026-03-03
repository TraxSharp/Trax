// ─────────────────────────────────────────────────────────────────────────────
// Trax Game Server — GraphQL API
//
// A game server API powered by HotChocolate GraphQL. Handles lightweight
// operations directly and hands off heavy work to the scheduler via the
// queueTrain mutation. This process does NOT run a scheduler — start the
// Scheduler project alongside this one.
//
// Authentication: fake API key via X-Api-Key header (for demonstration only)
//   Admin key:  admin-key-do-not-use-in-production  (roles: Admin, Player)
//   Player key: player-key-do-not-use-in-production (role: Player)
//
// Prerequisites:
//   1. Start Postgres:  cd Trax.Samples && docker compose up -d
//   2. Pack local:      ./pack-local.sh
//   3. Start scheduler: dotnet run --project samples/Trax.Samples.GameServer.Scheduler
//   4. Start API:       dotnet run --project samples/Trax.Samples.GameServer.GraphQL
//
// Try it:
//   Open http://localhost:5002/trax/graphql in a browser for Banana Cake Pop IDE
//
//   Or via curl (include API key header):
//
//   # Discover trains
//   curl -H "X-Api-Key: player-key-do-not-use-in-production" \
//        -X POST http://localhost:5002/trax/graphql \
//        -H "Content-Type: application/json" \
//        -d '{"query":"{ trains { serviceTypeName inputTypeName requiredPolicies requiredRoles inputSchema { name typeName } } }"}'
//
//   # Run a lightweight train directly
//   curl -H "X-Api-Key: player-key-do-not-use-in-production" \
//        -X POST http://localhost:5002/trax/graphql \
//        -H "Content-Type: application/json" \
//        -d '{"query":"mutation { runTrain(trainName: \"Trax.Samples.GameServer.Trains.Players.LookupPlayer.ILookupPlayerTrain\", input: {playerId: \"player-42\"}) { metadataId } }"}'
//
//   # Queue a heavy train for the scheduler
//   curl -H "X-Api-Key: player-key-do-not-use-in-production" \
//        -X POST http://localhost:5002/trax/graphql \
//        -H "Content-Type: application/json" \
//        -d '{"query":"mutation { queueTrain(trainName: \"Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult.IProcessMatchResultTrain\", input: {region: \"na\", matchId: \"match-999\", winnerId: \"player-1\", loserId: \"player-2\", winnerScore: 100, loserScore: 30}, priority: 10) { workQueueId externalId } }"}'
//
//   # Health check (no auth required)
//   curl http://localhost:5002/trax/health
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.AspNetCore.Authentication;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.GameServer;
using Trax.Samples.GameServer.Auth;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Authentication — fake API key for demonstration ──────────────────────
builder
    .Services.AddAuthentication(ApiKeyDefaults.AuthenticationScheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>(
        ApiKeyDefaults.AuthenticationScheme,
        null
    );

// ── Authorization policies ──────────────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
});

// ── Register Trax Effect + Mediator (trains, bus, discovery, execution) ──
// No AddScheduler() — the scheduler is a separate process.
// All trains (API + scheduler) are registered so queueTrain can discover them.
builder.Services.AddTraxEffects(options =>
    options
        .AddServiceTrainBus(assemblies: [typeof(ManifestNames).Assembly])
        .AddPostgresEffect(connectionString)
        .AddEffectDataContextLogging()
        .AddJsonEffect()
        .SaveTrainParameters()
);

// ── Register GraphQL API ────────────────────────────────────────────────
builder.Services.AddTraxGraphQL();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

// ── Map endpoints ───────────────────────────────────────────────────────
// No endpoint-level RequireAuthorization() here — Banana Cake Pop (the
// GraphQL IDE) is served at the same path and browsers can't send the
// X-Api-Key header on page load. Per-train auth via [TraxAuthorize] still
// protects individual operations. For production, use cookie-based auth
// or a separate IDE path.
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();
