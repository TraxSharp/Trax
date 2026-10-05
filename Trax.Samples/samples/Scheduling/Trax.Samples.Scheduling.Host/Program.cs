// ─────────────────────────────────────────────────────────────────────────────
// Trax Scheduling sample
//
// One process that schedules trains, runs them on local workers, retries the ones that fail
// and dead-letters the ones that keep failing, with two ways to watch and steer it: the Trax
// GraphQL operations surface and the Trax Dashboard.
//
//   refresh-exchange-rates   every 5 seconds
//     ├── reprice-catalog    dependent: after each successful refresh
//     └── alert-rate-spike   dormant dependent: only when a refresh activates it
//   send-daily-digest        cron, 07:00 UTC every day
//   send-launch-announcement once, 10 seconds after startup, then disabled for good
//   import-supplier-feed     every 15 seconds, fails while the supplier is down:
//                            2 retries with backoff, then a dead letter
//
// Run it (from the Trax.Samples folder):
//   docker compose up -d database
//   dotnet run --project samples/Scheduling/Trax.Samples.Scheduling.Host
//
// Dashboard:  http://localhost:5230/trax            (Development only)
// GraphQL:    http://localhost:5230/trax/graphql    header X-Api-Key: operator-key-do-not-use-in-production
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Scheduling;
using Trax.Samples.Scheduling.Host;
using Trax.Samples.Scheduling.Services;
using Trax.Samples.Scheduling.Trains.AlertRateSpike;
using Trax.Samples.Scheduling.Trains.ImportSupplierFeed;
using Trax.Samples.Scheduling.Trains.RefreshExchangeRates;
using Trax.Samples.Scheduling.Trains.RepriceCatalog;
using Trax.Samples.Scheduling.Trains.SendDailyDigest;
using Trax.Samples.Scheduling.Trains.SendLaunchAnnouncement;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

// The two simulated outside services the trains call. Singletons, so their state (the rate
// cycle, the supplier outage) lasts for the life of the process.
builder.Services.AddSingleton<ExchangeRateFeed>();
builder.Services.AddSingleton<SupplierFeed>();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                // Postgres holds the manifests, the work queue, every run and the dead letters,
                // and its background_job table feeds the local workers.
                .UsePostgres(connectionString)
                // Store each run's input and output, so the dashboard can show them.
                .SaveTrainParameters()
        )
        .AddMediator(typeof(ManifestNames).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                // ── Demo speed ──────────────────────────────────────────────────
                // These make a retry and a dead letter happen within seconds instead of
                // minutes. The production defaults are in the comments.
                .ManifestManagerPollingInterval(TimeSpan.FromSeconds(1)) // default 5 s
                .JobDispatcherPollingInterval(TimeSpan.FromSeconds(1)) // default 2 s
                .DefaultRetryDelay(TimeSpan.FromSeconds(2)) // default 5 min
                .RetryBackoffMultiplier(2.0) // default 2.0: 2 s, then 4 s, then 8 s...
                .MaxRetryDelay(TimeSpan.FromSeconds(30)) // default 1 h
                .ConfigureLocalWorkers(workers =>
                    workers.PollingInterval = TimeSpan.FromMilliseconds(250) // default 1 s
                )
                // ── Metadata cleanup ────────────────────────────────────────────
                // Every run leaves a metadata row, and refresh-exchange-rates runs every
                // five seconds. Keep the sample's own runs for a day; the scheduler's
                // internal trains are always swept, at RetentionPeriod.
                .AddMetadataCleanup(cleanup =>
                {
                    cleanup.RetentionPeriod = TimeSpan.FromMinutes(30);
                    cleanup.AddTrainType<IRefreshExchangeRatesTrain>(TimeSpan.FromDays(1));
                    cleanup.AddTrainType<IRepriceCatalogTrain>(TimeSpan.FromDays(1));
                })
                // ── Interval, with a dependent and a dormant dependent ─────────
                .Schedule<IRefreshExchangeRatesTrain>(
                    ManifestNames.RefreshExchangeRates,
                    new RefreshExchangeRatesInput { BaseCurrency = "USD" },
                    Every.Seconds(5)
                )
                // Runs after every successful refresh. No schedule of its own.
                .ThenInclude<IRepriceCatalogTrain>(
                    ManifestNames.RepriceCatalog,
                    new RepriceCatalogInput { Catalog = "storefront" }
                )
                // Include parents from the Schedule above, not from reprice-catalog.
                // Dormant: runs only when FlagSpikeJunction activates it, with its own input.
                .Include<IAlertRateSpikeTrain>(
                    ManifestNames.AlertRateSpike,
                    new AlertRateSpikeInput(),
                    options => options.Dormant()
                )
                // ── Cron ────────────────────────────────────────────────────────
                // Cron is evaluated in UTC. A new cron manifest first runs at its next
                // occurrence, not at startup.
                .Schedule<ISendDailyDigestTrain>(
                    ManifestNames.SendDailyDigest,
                    new SendDailyDigestInput { Audience = "subscribers" },
                    Cron.Daily(hour: 7)
                )
                // ── One-off ─────────────────────────────────────────────────────
                // Runs once, ten seconds after startup, then disables itself. Every start
                // re-seeds it and moves a one-off that has not run yet to ten seconds after
                // that start; one that already succeeded stays disabled and never runs again.
                .ScheduleOnce<ISendLaunchAnnouncementTrain>(
                    ManifestNames.SendLaunchAnnouncement,
                    new SendLaunchAnnouncementInput(),
                    TimeSpan.FromSeconds(10)
                )
                // ── Retries and a dead letter ───────────────────────────────────
                // MaxRetries(2): the first run and two retries, then the manifest is
                // dead-lettered and skipped until an operator requeues or acknowledges it.
                // A retry is the manifest's next due run held back by the backoff, so a
                // failure is retried as soon as the manifest is due and the backoff has passed.
                .Schedule<IImportSupplierFeedTrain>(
                    ManifestNames.ImportSupplierFeed,
                    new ImportSupplierFeedInput { Supplier = "acme" },
                    Every.Seconds(15),
                    options => options.MaxRetries(2)
                )
        )
);

// ── The GraphQL operations surface ──────────────────────────────────────────
// `operations` lists manifests, runs and dead letters, and triggers, disables and requeues
// them. It is off until exposed, and exposing it without a gate refuses to start the host.
// GateOperations puts the Operator role in front of the whole namespace.
//
// NO WARRANTY: the demo key below is registered only in Development. In any other environment
// no credential exists, so every operation is refused until you register real ones.
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoKeys.OperatorKey, id: "operator", DemoKeys.OperatorRole)
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: DemoKeys.OperatorRole)
);

// ── The dashboard ───────────────────────────────────────────────────────────
// The dashboard can trigger, cancel and requeue work and change scheduler settings. This
// sample puts no sign-in in front of it, so it is served only in Development. Anywhere else,
// gate it with RequirePolicy or RequireRoles before serving it.
if (builder.Environment.IsDevelopment())
    builder.AddTraxDashboard(options => options.AllowAnonymousDashboard());

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.UseTraxDashboard();

app.UseTraxGraphQL();

app.Run();

namespace Trax.Samples.Scheduling.Host
{
    public partial class Program;
}
