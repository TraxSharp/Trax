// ─────────────────────────────────────────────────────────────────────────────
// Trax JobHunt Hub, Phase 0 baseline.
//
// A self-hosted job hunt CRM built on the full Trax stack: Postgres effects,
// mediator, scheduler (added in a later phase), GraphQL API, lifecycle hooks
// (added in a later phase), and the Trax dashboard mounted at /trax.
//
// Phase 0 ships the baseline only: no trains, no scheduler, no hooks. Just
// the wiring required to run the GraphQL endpoint, authenticate via fake
// X-Api-Key headers, and auto-migrate the JobHunt domain database.
//
// Authentication: fake API key via X-Api-Key header (for demonstration only)
//   alice-key   to user "alice"   (display name: Alice)
//   bob-key     to user "bob"     (display name: Bob)
//   charlie-key to user "charlie" (display name: Charlie)
//
// Prerequisites:
//   1. Start Postgres:  cd Trax.Samples && docker compose up -d
//   2. Pack local:      ./pack-local.sh
//   3. Run:             dotnet run --project samples/JobHunt/Trax.Samples.JobHunt.Hub
//
// Open http://localhost:5310/trax/graphql for Banana Cake Pop, or
// http://localhost:5310/trax for the dashboard.
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.JobHunt.Auth;
using Trax.Samples.JobHunt.Data;
using Trax.Samples.JobHunt.Hooks;
using Trax.Samples.JobHunt.Providers.Contact;
using Trax.Samples.JobHunt.Providers.Email;
using Trax.Samples.JobHunt.Providers.Llm;
using Trax.Samples.JobHunt.Providers.Scraper;
using Trax.Samples.JobHunt.Subscriptions;
using Trax.Samples.JobHunt.Trains.AddJob;
using Trax.Samples.JobHunt.Trains.MonitorAllActiveJobs;
using Trax.Samples.JobHunt.Trains.MonitorAllWatchedCompanies;
using Trax.Samples.JobHunt.Trains.MonitorJob;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

// ── JobHunt domain data layer ───────────────────────────────────────────────
// Trax effect tables live in the `trax` schema; JobHunt domain tables live in
// `public`. Both share the same Postgres database.
builder.Services.AddDbContext<JobHuntDbContext>(options => options.UseNpgsql(connectionString));

// ── Authentication, fake API key for demonstration (NO WARRANTY, see SECURITY-DISCLAIMER.md) ──
builder.Services.AddTraxApiKeyAuth<JobHuntApiKeyResolver>();
builder.Services.AddAuthorization();

// ── Trax: Effects + Mediator + Scheduler ────────────────────────────────────
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddJson()
                .SaveTrainParameters()
                .AddJunctionProgress()
                .AddDataContextLogging()
                .AddLifecycleHook<JobHuntLifecycleHookFactory>()
        )
        .AddMediator(typeof(IAddJobTrain).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                .AddMetadataCleanup(cleanup =>
                {
                    cleanup.AddTrainType<IMonitorJobTrain>();
                    cleanup.AddTrainType<IMonitorAllActiveJobsTrain>();
                    cleanup.AddTrainType<IMonitorAllWatchedCompaniesTrain>();
                })
                .Schedule<
                    IMonitorAllActiveJobsTrain,
                    MonitorAllActiveJobsInput,
                    MonitorAllActiveJobsOutput
                >("MonitorAllActiveJobs", new MonitorAllActiveJobsInput(), Every.Hours(24))
                .Schedule<
                    IMonitorAllWatchedCompaniesTrain,
                    MonitorAllWatchedCompaniesInput,
                    MonitorAllWatchedCompaniesOutput
                >(
                    "MonitorAllWatchedCompanies",
                    new MonitorAllWatchedCompaniesInput(),
                    Every.Hours(6)
                )
        )
);

// ── Pluggable providers ─────────────────────────────────────────────────────
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.AddHttpClient<ILlmProvider, OllamaLlmProvider>();
builder.Services.AddHttpClient<IJobScraper, GenericHtmlScraper>();
builder.Services.AddSingleton<IContactEnrichmentProvider, ManualContactProvider>();
builder.Services.AddSingleton<IEmailSender, StubEmailSender>();

// ── GraphQL API + subscriptions ─────────────────────────────────────────────
builder.Services.AddTraxGraphQL(graphql => graphql.AddTypeExtension<JobHuntSubscriptions>());

builder.Services.AddHealthChecks().AddTraxHealthCheck();

// ── Dashboard ───────────────────────────────────────────────────────────────
builder.AddTraxDashboard();

// ── CORS, allow React dev server ────────────────────────────────────────────
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

// ── Auto-migrate JobHunt domain schema on startup ───────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<JobHuntDbContext>();
    await db.Database.MigrateAsync();
}

// ── Map endpoints ───────────────────────────────────────────────────────────
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.UseTraxDashboard();
app.MapHealthChecks("/trax/health");

app.Run();

namespace Trax.Samples.JobHunt.Hub
{
    public partial class Program;
}
