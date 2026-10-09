// ─────────────────────────────────────────────────────────────────────────────
// Trax Recovery: watch a train recover from a crash without asking the model again
//
// One process: GraphQL API + scheduler + local workers + dashboard, on Postgres.
// Three scenario trains ask a decider (a stand-in model by default) and then crash on a later step;
// the topic map runs three signals side by side and crashes one of the branches.
// The manifest's automatic retry replays the recorded decisions instead of asking again, and every
// junction, question and track is published live as a junction event.
//
// Prerequisites (from the Trax.Samples folder):
//   docker compose up -d database
//   dotnet run --project samples/Recovery/Trax.Samples.Recovery.Api
//   cd samples/Recovery/Trax.Samples.Recovery.Client && npm ci && npm run dev
// Then open http://localhost:5173. The dashboard is at http://localhost:5260/trax (Development only).
//
// Auth: two demo keys, Development only:
//   X-Api-Key: recovery-operator-key-do-not-use-in-production   (role Operator: what the page uses)
//   X-Api-Key: recovery-viewer-key-do-not-use-in-production     (role Viewer: the broadcast view)
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Core.Decisions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Decisions.SystemOne.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;
using Trax.Samples.Recovery;
using Trax.Samples.Recovery.Api;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Corpus;
using Trax.Samples.Recovery.Faults;
using Trax.Samples.Recovery.Index;
using Trax.Samples.Recovery.Machines;
using Trax.Samples.Recovery.Model;
using Trax.Samples.Recovery.Records;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Set ConnectionStrings:TraxDatabase.");

// ── The demo's own services ─────────────────────────────────────────────────
var pace = builder.Configuration.GetSection(DemoPace.Section).Get<DemoPace>() ?? new DemoPace();
builder.Services.AddSingleton(pace);
builder.Services.AddSingleton<FaultInjector>();
builder.Services.AddSingleton<CaseFiles>();

// The topic map's papers and the pairs it writes, in the topic_map schema beside Trax's tables.
builder.Services.AddDomainDataContext<ITopicMapDbContext, TopicMapDbContext>(options =>
    options.UseNpgsql(connectionString)
);

// Every build writes a new map; the sweep deletes the maps no topic-map draft points at any more.
builder.Services.AddSingleton<TopicMapSweeper>();
builder.Services.AddHostedService(services => new TopicMapSweepService(
    services.GetRequiredService<TopicMapSweeper>(),
    builder.Configuration.GetValue("Recovery:TopicMapSweepInterval", TimeSpan.FromMinutes(5)),
    services.GetRequiredService<ILogger<TopicMapSweepService>>()
));

// ── The model ───────────────────────────────────────────────────────────────
// "Demo" (the default) answers deterministically after 0.5 to 1.5 seconds. "Nimble" asks a Nimble
// server you run (Recovery:Nimble:Endpoint, the full URL of its POST /v1/systemone).
var model = builder.Configuration["Recovery:Model"] ?? "Demo";
var useNimble = model.Equals("Nimble", StringComparison.OrdinalIgnoreCase);
if (!useNimble)
    builder.Services.AddSingleton<IDecider, DemoDecider>();

// ── Trax ────────────────────────────────────────────────────────────────────
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
        {
            var configured = effects
                .UsePostgres(connectionString)
                .AddJson()
                // requeueExecution reads a run's saved input.
                .SaveTrainParameters()
                // Writes each decision to trax.decision and replays it on a retry or a requeue.
                // The refund's state holds a [TraxSensitive] member, so states are hashed under a
                // key: Trax:Decisions:StateHashKey (appsettings.Development.json holds a demo one).
                .AddDecisionRecording()
                // Publishes each junction, question and track live, and stores it in
                // trax.junction_run. Only EffectJunctions are steps.
                .AddJunctionEvents()
                // Records the running junction and reads a run's cancel flag between junctions, so
                // a machine leaving a state stops the run it invoked on whichever host runs it.
                .AddJunctionProgress();

            return useNimble
                ? configured.AddNimbleDecider(o =>
                    o.Endpoint = new Uri(
                        builder.Configuration["Recovery:Nimble:Endpoint"]
                            ?? throw new InvalidOperationException(
                                "Recovery:Model is Nimble: set Recovery:Nimble:Endpoint."
                            )
                    )
                )
                : configured;
        })
        // Two state machines: one system-owned instance per index partition, which ingests it, and
        // the topic map wizard each user drives, whose Building state builds the map. Before
        // AddMediator, which routes their four stateMachine mutations.
        .AddStateMachines(typeof(TopicMapMachine).Assembly)
        .AddMediator(typeof(DemoDecider).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                // Demo speed only. The defaults (5 s and 2 s polling, a 5 minute retry delay that
                // doubles) suit production; these let a person watch a retry within seconds.
                .ManifestManagerPollingInterval(TimeSpan.FromSeconds(1))
                .JobDispatcherPollingInterval(TimeSpan.FromSeconds(1))
                .DefaultRetryDelay(TimeSpan.FromSeconds(4))
                .RetryBackoffMultiplier(1.0)
                .MaxRetryDelay(TimeSpan.FromSeconds(10))
                .ConfigureLocalWorkers(workers =>
                    workers.PollingInterval = TimeSpan.FromMilliseconds(250)
                )
                // Polling every second, the scheduler records two runs of its own each second,
                // about 170,000 a day. Sweep those; the scenario trains are not listed, so their
                // runs, junction runs and decisions stay for the page to read.
                .AddMetadataCleanup(cleanup =>
                {
                    cleanup.RetentionPeriod = builder.Configuration.GetValue(
                        "Recovery:SchedulerRunRetention",
                        TimeSpan.FromMinutes(10)
                    );
                    cleanup.CleanupInterval = builder.Configuration.GetValue(
                        "Recovery:CleanupInterval",
                        TimeSpan.FromMinutes(1)
                    );
                })
        )
);

// ── Authentication: two demo keys, Development only ──────────────────────────
// The page needs each question's answer and the names of the junctions on a track, which only the
// operations view of onJunctionEvent carries. A key carrying "do-not-use-in-production" refuses to
// start outside Development, and outside it no key exists, so the operations namespace is closed.
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoKeys.Operator, id: "operator", RecoveryRoles.Operator)
            .Add(DemoKeys.Viewer, id: "viewer", RecoveryRoles.Viewer)
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// A topic map draft belongs to the caller who saved it: each demo key has drafts of its own.
builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();

// ── GraphQL ─────────────────────────────────────────────────────────────────
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        // operations.junctionRuns, requeueExecution(askAfresh:), triggerManifest(askAfresh:),
        // operations.executions(manifestId:), and the operations view of onJunctionEvent.
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: RecoveryRoles.Operator)
);
builder.Services.AddHealthChecks().AddTraxHealthCheck();

// ── Dashboard, Development only ─────────────────────────────────────────────
if (builder.Environment.IsDevelopment())
    builder.AddTraxDashboard(dashboard => dashboard.AllowAnonymousDashboard());

// ── CORS for the Vite dev server ────────────────────────────────────────────
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

// Creates the topic_map schema and loads the canned corpus, skipping papers already there, then the
// canned index records partitions are ingested from, skipping records already there.
await CorpusSeeder.SeedAsync(app.Services);
await IndexSeeder.SeedAsync(app.Services);

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
    app.UseTraxDashboard();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();

namespace Trax.Samples.Recovery.Api
{
    public partial class Program;
}
