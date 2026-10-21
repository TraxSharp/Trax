// ─────────────────────────────────────────────────────────────────────────────
// Trax Board: agents work a ticket through the lanes; the server decides every move
//
// One process: GraphQL API + scheduler + local workers + operations console, on Postgres.
// Each ticket is a system-owned TicketMachine instance that no client can read or move. A worker claims a ticket in
// its lane (claimWork), drives an AssignmentMachine of its own through the generated client, and sends its evidence
// as an event. The state that event enters runs the lane's check train on the server, which runs the evidence itself
// and moves the ticket only when it holds.
//
// Prerequisites (from the Trax.Samples folder):
//   docker compose up -d database
//   dotnet run --project samples/Board/Trax.Samples.Board.Api
//   cd samples/Board/Trax.Samples.Board.Agents && npm ci && npm run scenario -- boundary
//   cd samples/Board/Trax.Samples.Board.Client && npm ci && npm run dev
// Then open http://localhost:5173. The operations console is at http://localhost:5290/trax/dashboard
// (Development only; paste the operator key).
//
// Auth: one demo key per client, Development only (BoardKeys):
//   triage-bot (triage), repro-bot (reproducer), fix-bot (fixer), qa-bot (qa), Sam (reviewer, reporter), operator.
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Auth.ApiKey;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Effect.Configuration.ClientExport;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Board.Api;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Tickets;
using Trax.Scheduler.Extensions;
using Trax.StateMachine.Persistence;

var builder = WebApplication.CreateBuilder(args);

// An export of the client manifest (--trax-export-client) opens no database, so it needs no connection string.
var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? (
        TraxClientExport.Requested
            ? "Host=localhost"
            : throw new InvalidOperationException("Set ConnectionStrings:TraxDatabase.")
    );

// Reads the board and moves tickets, as the host. Every train that touches a ticket goes through it.
builder.Services.AddScoped<TicketBoard>();

// ── Trax ────────────────────────────────────────────────────────────────────
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddJson()
                .SaveTrainParameters()
                // A check train is invoked by a machine state: leaving the state cancels it on any host.
                .AddJunctionProgress()
        )
        // Two machines. The ticket is system-owned: host code alone moves it. The assignment is marked
        // [TraxMachine], so it is served as machines { assignment { start triage submitRepro close submitFix
        // submitChecks approve sendBack discard get } } and assignmentChanged, and each agent drives its own.
        .AddStateMachines(
            o =>
            {
                if (
                    builder.Configuration.GetValue<TimeSpan?>("Board:MachineSweepInterval") is
                    { } sweep
                )
                {
                    o.InvokeOutcomeSweepInterval = sweep;
                    o.InvokeOutcomeSweepIntervalWhileListening = sweep;
                }
            },
            typeof(AssignmentMachine).Assembly
        )
        .AddMediator(typeof(AssignmentMachine).Assembly)
        // The scheduler runs the check trains the assignments invoke. Demo speed: a check answers within a second.
        .AddScheduler(scheduler =>
            scheduler
                .ManifestManagerPollingInterval(TimeSpan.FromSeconds(1))
                .JobDispatcherPollingInterval(TimeSpan.FromSeconds(1))
                .ConfigureLocalWorkers(workers =>
                    workers.PollingInterval = TimeSpan.FromMilliseconds(250)
                )
                .AddMetadataCleanup(cleanup =>
                {
                    cleanup.RetentionPeriod = TimeSpan.FromMinutes(10);
                    cleanup.CleanupInterval = TimeSpan.FromMinutes(1);
                })
        )
);

// ── Authentication: one demo key per client, Development only ───────────────
// The principal id is the worker's name: it is who an assignment belongs to and who a claim names. A key carrying
// "do-not-use-in-production" refuses to start outside Development, and outside it no key exists.
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(BoardKeys.TriageBot, id: "triage-bot", BoardRoles.Triage)
            .Add(BoardKeys.ReproBot, id: "repro-bot", BoardRoles.Reproducer)
            .Add(BoardKeys.FixBot, id: "fix-bot", BoardRoles.Fixer)
            .Add(BoardKeys.QaBot, id: "qa-bot", BoardRoles.Qa)
            .Add(BoardKeys.Sam, id: "sam", BoardRoles.Reviewer, BoardRoles.Reporter)
            .Add(BoardKeys.Operator, id: "operator", BoardRoles.Operator)
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// An assignment belongs to the worker who started it.
builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();

// ── GraphQL ─────────────────────────────────────────────────────────────────
// Only persisted documents run, as in production. Trax registers the machine's documents itself; the trains the
// agents and the page call are reached through the documents they wrote, which trax client --documents writes into
// each one's trax.documents.json. Board:RequirePersistedOperations turns that off for the E2E suite's hand-written
// requests, in Development only.
var requirePersisted =
    !builder.Environment.IsDevelopment()
    || builder.Configuration.GetValue("Board:RequirePersistedOperations", true);
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: BoardRoles.Operator)
        .UsePersistedOperations(persisted =>
        {
            persisted
                .SingleNode()
                .RequirePersisted(requirePersisted)
                .LogNonPersistedRequests(!requirePersisted)
                .GateManagement(roles: BoardRoles.Operator);
            foreach (var file in new[] { "agents.documents.json", "client.documents.json" })
                if (File.Exists(Path.Combine(AppContext.BaseDirectory, file)))
                    persisted.ClientDocuments(Path.Combine(AppContext.BaseDirectory, file));
            if (requirePersisted)
                persisted.DisableIntrospection();
        })
);
builder.Services.AddHealthChecks().AddTraxHealthCheck();

// ── CORS for the board page's Vite dev server ───────────────────────────────
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
    )
);

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

if (app.Environment.IsDevelopment())
    app.UseTraxApiDashboard(dashboard => dashboard.AllowAnonymousShell());

// Serves, or writes the client manifest and exits when started with --trax-export-client <path>.
await app.RunTraxAsync(args);

namespace Trax.Samples.Board.Api
{
    public partial class Program;
}
