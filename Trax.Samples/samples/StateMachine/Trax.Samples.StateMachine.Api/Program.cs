// ─────────────────────────────────────────────────────────────────────────────
// Trax State Machine sample — a GraphQL host over two fluent machines.
//
// Two machines, authored fluently in Trax.Samples.StateMachine, are discovered with one line
// (AddTraxStateMachines) and driven through the four generic `stateMachine` mutations:
//   turnstile  Locked ⇄ Unlocked                (no effect)
//   checkout   Cart → Review → Paid             (Paid committed, one exactly-once charge on Pay)
//
// Authentication: fake API key via X-Api-Key header (demonstration only)
//   alice-key → user "alice"
//   bob-key   → user "bob"
//
// Run it:
//   1. Start Postgres:  cd Trax.Samples && docker compose up -d
//   2. Pack local:      ./pack-local.sh
//   3. Start the host:  dotnet run --project samples/StateMachine/Trax.Samples.StateMachine.Api
//
// Then open http://localhost:5220/trax/graphql (Banana Cake Pop). Send X-Api-Key: alice-key and try:
//
//   # What machines are available?
//   { discover { stateMachine { listMachines { machines { name hasEffect } } } } }
//
//   # Save a fresh checkout draft, then advance it server-side. Use one id (a UUID) throughout.
//   mutation { dispatch { stateMachine { saveSnapshot(input: {
//     machine: "checkout", id: "11111111-1111-1111-1111-111111111111",
//     snapshot: "{\"machine\":\"checkout\",\"version\":1,\"state\":\"Review\",\"context\":{\"items\":[\"book\"],\"receipt\":null}}"
//   }) { output { snapshot problem { code } } } } } }
//
//   # Charge exactly once (state-gated + idempotent). A second send does not re-charge.
//   mutation { dispatch { stateMachine { sendSnapshot(input: {
//     machine: "checkout", id: "11111111-1111-1111-1111-111111111111", requestId: "pay-1"
//   }) { output { snapshot problem { code } } } } } }
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Persistence.Mutations;
using Trax.Mediator.Extensions;
using Trax.Samples.StateMachine;
using Trax.Samples.StateMachine.Api;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? "Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123";

builder.Services.AddLogging(logging => logging.AddConsole());

// Fake API keys for the demo (NO WARRANTY — see the samples security disclaimer).
builder.Services.AddTraxApiKeyAuth(keys =>
    keys.Add("alice-key", id: "alice", "User").Add("bob-key", id: "bob", "User")
);
builder.Services.AddAuthorization();

// Trax + the state machine. The mediator scan includes StateMachineMutations.Assembly so the four generic
// mutations route by input type; AddTraxStateMachines discovers the machines in the sample library and
// wires the store, the effect-claim ledger, the exactly-once runner, and the registry.
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString).AddJson())
        .AddMediator(typeof(TurnstileMachine).Assembly, StateMachineMutations.Assembly)
);
builder.Services.AddTraxStateMachines(typeof(TurnstileMachine).Assembly);

// The two host-supplied bindings a machine can't know: map auth to a user key, and the charge impl.
builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
builder.Services.AddScoped<ICharge, LoggingCharge>();

// The snapshot store's DbContext. The snapshot_draft + effect_claim tables are created by the
// Trax migration set: UsePostgres above runs DbUp, which applies 040_state_machine_snapshots.sql.
// Nothing here creates tables — `docker compose up -d` (a `trax` database) is all that's needed.
builder.Services.AddDbContext<SnapshotDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddTraxGraphQL(graphql => graphql);
builder.Services.AddHealthChecks().AddTraxHealthCheck();

// Allow the Vite dev server (the web/ frontend) to call the API. The frontend authenticates with the
// X-Api-Key header, not cookies, so credentials are not needed.
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()
    )
);

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();

namespace Trax.Samples.StateMachine.Api
{
    public partial class Program;
}
