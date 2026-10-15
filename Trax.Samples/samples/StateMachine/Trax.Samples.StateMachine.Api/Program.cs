// ─────────────────────────────────────────────────────────────────────────────
// Trax State Machine sample: a GraphQL host over two fluent machines.
//
// Two machines, authored declaratively in Trax.Samples.StateMachine, are discovered with one line
// (AddStateMachines) and exposed, so each is served as its own typed operations under the `machines` root. A
// client starts an instance with `start`, which mints its id from the machine's declared start, and moves it with
// one mutation per event; the effect's event runs the charge on the server:
//   turnstile  Locked ⇄ Unlocked                (no effect)
//   checkout   Cart → Review → Paid             (Paid committed, one exactly-once charge on Pay)
//
// Authentication: fake API key via X-Api-Key header (demonstration only)
//   alice-key-do-not-use-in-production → user "alice"
//   bob-key-do-not-use-in-production   → user "bob"
//
// Run it (from Trax.Samples/):
//   1. docker compose up -d
//   2. dotnet run --project samples/StateMachine/Trax.Samples.StateMachine.Api
//   3. cd samples/StateMachine/web && npm install && npm run dev
//
// The host serves persisted documents only: the page sends each operation by the id its generated client holds, and a
// document sent as text is refused. To regenerate the client after changing a machine or a train:
//   (cd samples/StateMachine/Trax.Samples.StateMachine.Api && dotnet run -- --trax-export-client ../web/src/trax/manifest.json)
//   trax client --manifest samples/StateMachine/web/src/trax/manifest.json --out samples/StateMachine/web/src/trax
//
// Docs: https://traxsharp.net/docs/samples/state-machine
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;
using Trax.Samples.StateMachine;
using Trax.Samples.StateMachine.Api;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? "Host=localhost;Port=5432;Database=trax_statemachine;Username=trax;Password=trax123";

builder.Services.AddLogging(logging => logging.AddConsole());

// Fake API keys for the demo (NO WARRANTY — see the samples security disclaimer).
// The demo keys are published in this repository, so they are registered only in Development
// (Properties/launchSettings.json sets it for `dotnet run`). Anywhere else no credential exists
// until you register real ones, and every [TraxAuthorize] operation is refused.
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add("alice-key-do-not-use-in-production", id: "alice", "User")
            .Add("bob-key-do-not-use-in-production", id: "bob", "User")
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// Trax + the state machine, in one builder chain. AddStateMachines discovers the machines in the sample
// library, wires the store / effect-claim ledger / exactly-once runner / registry against the Postgres
// provider above, and contributes the trains every machine operation runs through to the mediator scan. Both
// machines are marked [TraxMachine], so clients reach each through its own typed operations; a machine not marked
// is not served to a client.
// (Call AddStateMachines before AddMediator: the mediator builds its route registry when it runs.)
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString).AddJson())
        .AddStateMachines(
            o =>
            {
                // Alice and Bob are two users every visitor of the demo shares, and the server caps the drafts
                // one user may hold (50 by default). A draft a walkthrough or a closed browser leaves behind
                // expires after a day of no activity rather than the default 30, so a demo database that lives
                // for weeks does not fill the cap. The cap itself stays on.
                o.DraftTtl = TimeSpan.FromDays(1);
            },
            typeof(TurnstileMachine).Assembly
        )
        .AddMediator(typeof(TurnstileMachine).Assembly)
);

// The two host-supplied bindings a machine can't know: map auth to a user key, and the charge impl.
builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
builder.Services.AddSingleton<SimulatedPaymentProvider>();
builder.Services.AddScoped<ICharge, LoggingCharge>();

// Exposed machines and trains are served as persisted documents the host generates and registers, one per operation,
// whose ids carry the machine's version or the train's shape. The host serves nothing else: a document sent as text is
// refused, introspection included, and no document-management operations are exposed.
builder.Services.AddTraxGraphQL(graphql =>
    graphql.UsePersistedOperations(persisted =>
        persisted.SingleNode().RequirePersisted(true).DisableIntrospection()
    )
);
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

await app.RunTraxAsync(args);

namespace Trax.Samples.StateMachine.Api
{
    public partial class Program;
}
