// ─────────────────────────────────────────────────────────────────────────────
// Trax Game Server — REST API
//
// A game server API that handles lightweight operations directly and hands off
// heavy work to the scheduler via POST /trains/queue. This process does NOT run
// a scheduler — start the Scheduler project alongside this one.
//
// Authentication: fake API key via X-Api-Key header (for demonstration only)
//   Admin key:  admin-key-do-not-use-in-production  (roles: Admin, Player)
//   Player key: player-key-do-not-use-in-production (role: Player)
//
// Prerequisites:
//   1. Start Postgres:  cd Trax.Samples && docker compose up -d
//   2. Pack local:      ./pack-local.sh
//   3. Start scheduler: dotnet run --project samples/Trax.Samples.GameServer.Scheduler
//   4. Start API:       dotnet run --project samples/Trax.Samples.GameServer.Rest
//
// Try it:
//
//   # Discover all available trains
//   curl -H "X-Api-Key: player-key-do-not-use-in-production" \
//        http://localhost:5000/trax/api/trains
//
//   # Run a lightweight train directly on the API (player lookup)
//   curl -H "X-Api-Key: player-key-do-not-use-in-production" \
//        -X POST http://localhost:5000/trax/api/trains/run \
//        -H "Content-Type: application/json" \
//        -d '{"trainName":"Trax.Samples.GameServer.Trains.Players.LookupPlayer.ILookupPlayerTrain","input":{"playerId":"player-42"}}'
//
//   # Queue a heavy train for the scheduler (match result processing)
//   curl -H "X-Api-Key: player-key-do-not-use-in-production" \
//        -X POST http://localhost:5000/trax/api/trains/queue \
//        -H "Content-Type: application/json" \
//        -d '{"trainName":"Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult.IProcessMatchResultTrain","input":{"region":"na","matchId":"match-999","winnerId":"player-1","loserId":"player-2","winnerScore":100,"loserScore":30},"priority":10}'
//
//   # Admin-only: ban a player (requires admin key)
//   curl -H "X-Api-Key: admin-key-do-not-use-in-production" \
//        -X POST http://localhost:5000/trax/api/trains/run \
//        -H "Content-Type: application/json" \
//        -d '{"trainName":"Trax.Samples.GameServer.Trains.Players.BanPlayer.IBanPlayerTrain","input":{"playerId":"player-42","reason":"Cheating"}}'
//
//   # Player trying admin action → 403 Forbidden
//   curl -H "X-Api-Key: player-key-do-not-use-in-production" \
//        -X POST http://localhost:5000/trax/api/trains/run \
//        -H "Content-Type: application/json" \
//        -d '{"trainName":"Trax.Samples.GameServer.Trains.Players.BanPlayer.IBanPlayerTrain","input":{"playerId":"player-42","reason":"Cheating"}}'
//
//   # No API key → 401 Unauthorized
//   curl http://localhost:5000/trax/api/trains
//
//   # Health check (no auth required)
//   curl http://localhost:5000/trax/health
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.AspNetCore.Authentication;
using Trax.Api.Extensions;
using Trax.Api.Rest.Extensions;
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
// All trains (API + scheduler) are registered so QueueAsync can discover them.
builder.Services.AddTraxEffects(options =>
    options
        .AddServiceTrainBus(assemblies: [typeof(ManifestNames).Assembly])
        .AddPostgresEffect(connectionString)
        .AddEffectDataContextLogging()
        .AddJsonEffect()
        .SaveTrainParameters()
);

// ── Register REST API ───────────────────────────────────────────────────
builder.Services.AddTraxRestApi();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

// ── Map endpoints — all require a valid API key ─────────────────────────
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxRestApi(configure: group => group.RequireAuthorization());
app.MapHealthChecks("/trax/health");

app.Run();
