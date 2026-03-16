// ─────────────────────────────────────────────────────────────────────────────
// Trax Game Server — GraphQL API
//
// A game server API powered by HotChocolate GraphQL. Handles lightweight
// operations directly and hands off heavy work to the scheduler via the
// dispatch { trainName(mode: QUEUE) } mutations. This process does NOT run a scheduler — start the
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
//   4. Start API:       dotnet run --project samples/Trax.Samples.GameServer.Api
//
// Try it:
//   Open http://localhost:5200/trax/graphql in a browser for Banana Cake Pop IDE
//
//   Or via curl (include API key header):
//
//   # Discover trains
//   curl -H "X-Api-Key: player-key-do-not-use-in-production" \
//        -X POST http://localhost:5200/trax/graphql \
//        -H "Content-Type: application/json" \
//        -d '{"query":"{ trains { serviceTypeName inputTypeName requiredPolicies requiredRoles inputSchema { name typeName } } }"}'
//
//   # Query a train directly (typed query from [TraxQuery])
//   curl -H "X-Api-Key: player-key-do-not-use-in-production" \
//        -X POST http://localhost:5200/trax/graphql \
//        -H "Content-Type: application/json" \
//        -d '{"query":"{ discover { lookupPlayer(input: {playerId: \"player-42\"}) { playerId rank wins losses rating } } }"}'
//
//   # Query model data directly with filtering and pagination ([TraxQueryModel])
//   curl -H "X-Api-Key: player-key-do-not-use-in-production" \
//        -X POST http://localhost:5200/trax/graphql \
//        -H "Content-Type: application/json" \
//        -d '{"query":"{ discover { playerRecords(first: 10, where: { rating: { gte: 1500 } }) { nodes { playerId displayName rating } pageInfo { hasNextPage endCursor } } } }"}'
//
//   # Queue a heavy train for the scheduler (typed mutation from [TraxMutation])
//   curl -H "X-Api-Key: player-key-do-not-use-in-production" \
//        -X POST http://localhost:5200/trax/graphql \
//        -H "Content-Type: application/json" \
//        -d '{"query":"mutation { dispatch { processMatchResult(input: {region: \"na\", matchId: \"match-999\", winnerId: \"player-1\", loserId: \"player-2\", winnerScore: 100, loserScore: 30}, mode: QUEUE, priority: 10) { externalId workQueueId } } }"}'
//
//   # Subscribe to real-time train lifecycle events (use Banana Cake Pop IDE):
//   #   subscription { onTrainStarted { metadataId trainName trainState timestamp } }
//   #   subscription { onTrainCompleted { metadataId trainName trainState timestamp } }
//   #   subscription { onTrainFailed { metadataId trainName failureJunction failureReason } }
//
//   # Health check (no auth required)
//   curl http://localhost:5200/trax/health
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
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
using Trax.Samples.GameServer.Data;
using Trax.Samples.GameServer.Data.Models;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

// ── CORS — allow the Trax website (local dev) to connect ──────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

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
// All trains (API + scheduler) are registered so the execution service can discover them.
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddDataContextLogging()
                .AddJson()
                .SaveTrainParameters()
        )
        .AddMediator(typeof(ManifestNames).Assembly)
);

// ── Register application DbContext for game data ────────────────────────
builder.Services.AddDbContextFactory<GameDbContext>(options => options.UseNpgsql(connectionString));

// ── Register GraphQL API with model query discovery ─────────────────────
builder.Services.AddTraxGraphQL(graphql => graphql.AddDbContext<GameDbContext>());
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

// ── Ensure game tables exist and seed sample data (demo only) ─────────
// EnsureCreated() no-ops when the database already exists (e.g. trax schema),
// so we create the game schema and tables via the model's GenerateCreateScript().
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    try
    {
        db.Database.ExecuteSqlRaw("CREATE SCHEMA IF NOT EXISTS game");
        var createScript = db.Database.GenerateCreateScript();
        db.Database.ExecuteSqlRaw(createScript);
    }
    catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P07")
    {
        // Tables already exist — ignore
    }

    if (!db.Players.Any())
    {
        var players = new[]
        {
            new PlayerRecord
            {
                PlayerId = "player-1",
                DisplayName = "AceSniper",
                Rank = 1,
                Wins = 142,
                Losses = 31,
                Rating = 2150,
            },
            new PlayerRecord
            {
                PlayerId = "player-2",
                DisplayName = "ShadowBlade",
                Rank = 2,
                Wins = 128,
                Losses = 40,
                Rating = 2020,
            },
            new PlayerRecord
            {
                PlayerId = "player-3",
                DisplayName = "IronClad",
                Rank = 3,
                Wins = 110,
                Losses = 55,
                Rating = 1890,
            },
            new PlayerRecord
            {
                PlayerId = "player-4",
                DisplayName = "StormRider",
                Rank = 4,
                Wins = 95,
                Losses = 60,
                Rating = 1780,
            },
            new PlayerRecord
            {
                PlayerId = "player-5",
                DisplayName = "FrostByte",
                Rank = 5,
                Wins = 88,
                Losses = 72,
                Rating = 1650,
            },
            new PlayerRecord
            {
                PlayerId = "player-6",
                DisplayName = "BlazeFury",
                Rank = 6,
                Wins = 76,
                Losses = 80,
                Rating = 1520,
            },
            new PlayerRecord
            {
                PlayerId = "player-7",
                DisplayName = "NightHawk",
                Rank = 7,
                Wins = 65,
                Losses = 85,
                Rating = 1430,
            },
            new PlayerRecord
            {
                PlayerId = "player-8",
                DisplayName = "VoidWalker",
                Rank = 8,
                Wins = 50,
                Losses = 90,
                Rating = 1310,
            },
        };
        db.Players.AddRange(players);

        db.Matches.AddRange(
            new MatchRecord
            {
                MatchId = "match-001",
                Region = "na",
                WinnerId = "player-1",
                LoserId = "player-2",
                WinnerScore = 100,
                LoserScore = 82,
                PlayedAt = DateTime.UtcNow.AddHours(-6),
            },
            new MatchRecord
            {
                MatchId = "match-002",
                Region = "eu",
                WinnerId = "player-3",
                LoserId = "player-4",
                WinnerScore = 75,
                LoserScore = 60,
                PlayedAt = DateTime.UtcNow.AddHours(-5),
            },
            new MatchRecord
            {
                MatchId = "match-003",
                Region = "na",
                WinnerId = "player-1",
                LoserId = "player-5",
                WinnerScore = 110,
                LoserScore = 45,
                PlayedAt = DateTime.UtcNow.AddHours(-4),
            },
            new MatchRecord
            {
                MatchId = "match-004",
                Region = "ap",
                WinnerId = "player-2",
                LoserId = "player-6",
                WinnerScore = 90,
                LoserScore = 70,
                PlayedAt = DateTime.UtcNow.AddHours(-3),
            },
            new MatchRecord
            {
                MatchId = "match-005",
                Region = "eu",
                WinnerId = "player-5",
                LoserId = "player-7",
                WinnerScore = 85,
                LoserScore = 80,
                PlayedAt = DateTime.UtcNow.AddHours(-2),
            },
            new MatchRecord
            {
                MatchId = "match-006",
                Region = "na",
                WinnerId = "player-4",
                LoserId = "player-8",
                WinnerScore = 95,
                LoserScore = 30,
                PlayedAt = DateTime.UtcNow.AddHours(-1),
            }
        );

        db.SaveChanges();
    }
}

// ── Map endpoints ───────────────────────────────────────────────────────
// No endpoint-level RequireAuthorization() here — Banana Cake Pop (the
// GraphQL IDE) is served at the same path and browsers can't send the
// X-Api-Key header on page load. Per-train auth via [TraxAuthorize] still
// protects individual operations. For production, use cookie-based auth
// or a separate IDE path.
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();
