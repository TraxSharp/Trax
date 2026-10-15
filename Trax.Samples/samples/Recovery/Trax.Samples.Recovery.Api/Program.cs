// ─────────────────────────────────────────────────────────────────────────────
// Trax Recovery: watch a train recover from a crash without asking the model again
//
// One process: GraphQL API + scheduler + local workers + dashboard, on Postgres.
// The refund train asks a decider (a stand-in model by default) twice and can crash before or after its
// checkpoint; the topic map runs three signals side by side and crashes one of the branches.
// The manifest's automatic retry replays the recorded decisions instead of asking again, and every
// junction, question and track is published live as a junction event.
//
// Prerequisites (from the Trax.Samples folder):
//   docker compose up -d database
//   dotnet run --project samples/Recovery/Trax.Samples.Recovery.Api
//   cd samples/Recovery/Trax.Samples.Recovery.Client && npm ci && npm run dev
// Then open http://localhost:5173. The dashboard is at http://localhost:5260/trax (Development only).
//
// Auth: three demo keys, Development only:
//   X-Api-Key: recovery-user-key-do-not-use-in-production       (role recovery-user: what the page uses)
//   X-Api-Key: recovery-operator-key-do-not-use-in-production   (roles Operator and recovery-user: the dashboard, the API suites)
//   X-Api-Key: recovery-viewer-key-do-not-use-in-production     (role Viewer: the broadcast view)
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Core.Decisions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Configuration.ClientExport;
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
using Trax.Samples.Recovery.Orders;
using Trax.Samples.Recovery.Records;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

// An export of the client manifest (--trax-export-client) opens no database, so it needs no connection string. Npgsql
// still wants a host when the export resolves the services, so it gets one with no database and no credential, which
// nothing connects to.
var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? (
        TraxClientExport.Requested
            ? "Host=localhost"
            : throw new InvalidOperationException("Set ConnectionStrings:TraxDatabase.")
    );

// ── The demo's own services ─────────────────────────────────────────────────
var pace = builder.Configuration.GetSection(DemoPace.Section).Get<DemoPace>() ?? new DemoPace();
builder.Services.AddSingleton(pace);
builder.Services.AddSingleton<FaultInjector>();
builder.Services.AddSingleton<CaseFiles>();

// The food order's restaurant, simulated, and how long a cart waits for its order (30 minutes unless
// Recovery:CartWait shortens it, as the recording does). Set before AddStateMachines builds the machine.
builder.Services.AddSingleton<Restaurant>();
builder.Services.AddSingleton<OrderOwners>();
if (builder.Configuration.GetValue<TimeSpan?>("Recovery:CartWait") is { } cartWait)
    OrderMachine.CartWait = cartWait;

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
        // Three state machines: one system-owned instance per index partition, which ingests it, the
        // topic map wizard each user drives, whose Building state builds the map, and a user's food
        // order, whose Ordered state delivers it. The topic map and the order are marked [TraxMachine], so
        // each is served as its own typed operations, machines { topicMap { start chooseFields back build
        // cancelBuild rebuild edit startOver discard get } } and machines { order { start addItem
        // placeOrder discard get } }, and clients drive them through those only: the generic stateMachine
        // mutations are not served. Before AddMediator, which routes the trains every machine operation
        // runs through.
        .AddStateMachines(
            o =>
            {
                // Every visitor of the demo's page signs in with the same user key, so all their topic maps
                // belong to one user and count against that one user's draft cap (50 by default). Starting
                // over leaves no draft behind: the page resets a draft a build began (StartOver), which the
                // server will not discard, and discards an earlier one. A draft a closed browser abandoned
                // stops counting against the cap after a day of no activity rather than the default 30, so a
                // demo database that lives for weeks does not fill it. One abandoned before a build is then
                // deleted; one a build moved is kept as the record of that build (one in Built until its 7-day
                // review timer moves it to Expired). The cap itself stays on.
                o.DraftTtl = TimeSpan.FromDays(1);
                // How often the reconciler sweeps for due timers and ended runs it was not told of. The
                // defaults suit the demo; a test host that moves a due time into the past behind the
                // reconciler's back sets this so the sweep finds it within the test's bound.
                if (
                    builder.Configuration.GetValue<TimeSpan?>("Recovery:MachineSweepInterval") is
                    { } sweep
                )
                {
                    o.InvokeOutcomeSweepInterval = sweep;
                    o.InvokeOutcomeSweepIntervalWhileListening = sweep;
                }
            },
            typeof(TopicMapMachine).Assembly
        )
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

// ── Authentication: three demo keys, Development only ────────────────────────
// The page signs in as a demo user and follows only its own runs, through runProgress, whose trains show it each
// answer and track ([TraxRunProgress(Answers = true)]). The operator's key is the dashboard's and the API suites'. A
// key carrying "do-not-use-in-production" refuses to start outside Development, and outside it no key exists, so the
// operations namespace is closed.
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoKeys.User, id: "demo-user", RecoveryRoles.User)
            .Add(DemoKeys.Operator, id: "operator", RecoveryRoles.Operator, RecoveryRoles.User)
            .Add(DemoKeys.Viewer, id: "viewer", RecoveryRoles.Viewer)
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// A topic map draft belongs to the caller who saved it: each demo key has drafts of its own.
builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();

// ── GraphQL ─────────────────────────────────────────────────────────────────
// The host runs only persisted documents, as it would in production: an ad-hoc document is refused on every
// transport, and so is introspection. Every document the page sends is generated and registered by Trax when the
// host starts, one per machine operation, exposed train and runProgress operation, and the page holds them in its
// generated client (src/trax, from the manifest --trax-export-client writes). The hand-written recovery_* documents
// an earlier version registered are deactivated (RetiredPageDocuments). Recovery:RequirePersistedOperations turns
// the refusal off, for a suite that drives the API with hand-written documents, in Development only: anywhere else
// the setting is ignored and only registered documents run.
var requirePersisted =
    !builder.Environment.IsDevelopment()
    || builder.Configuration.GetValue("Recovery:RequirePersistedOperations", true);
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        // operations.junctionRuns, requeueExecution(askAfresh:), triggerManifest(askAfresh:),
        // operations.executions(manifestId:), and the operations view of onJunctionEvent.
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: RecoveryRoles.Operator)
        .UsePersistedOperations(persisted =>
        {
            // The operator manages documents too: persistedOperations, behind its own gate.
            persisted
                .SingleNode()
                .RequirePersisted(requirePersisted)
                .LogNonPersistedRequests(!requirePersisted)
                .GateManagement(roles: RecoveryRoles.Operator);
            if (requirePersisted)
                persisted.DisableIntrospection();
        })
);
builder.Services.AddHostedService<RetiredPageDocuments>();
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
// canned index records partitions are ingested from, skipping records already there. An export of the client
// manifest (--trax-export-client) opens no database, so it seeds nothing.
if (!TraxClientExport.Requested)
{
    await CorpusSeeder.SeedAsync(app.Services);
    await IndexSeeder.SeedAsync(app.Services);
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
    app.UseTraxDashboard();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

// Serves, or writes the client manifest and exits when started with --trax-export-client <path>.
await app.RunTraxAsync(args);

namespace Trax.Samples.Recovery.Api
{
    public partial class Program;
}
