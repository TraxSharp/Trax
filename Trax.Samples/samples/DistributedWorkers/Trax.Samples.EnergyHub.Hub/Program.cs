// ─────────────────────────────────────────────────────────────────────────────
// Somerset Energy Hub: the hub (GraphQL API + scheduler + dashboard)
//
// The hub schedules work and serves the API; it does not run scheduled or queued
// jobs. OverrideSubmitter registers PostgresJobSubmitter on its own, so every job
// the scheduler dispatches is written to trax.background_job and no local worker
// starts. A separate Worker process (Trax.Samples.EnergyHub.Worker) claims those
// rows and runs the trains. Both processes publish and receive lifecycle events
// over RabbitMQ, so a subscription on the hub sees runs that happened on a worker.
//
// What still runs on the hub: the one [TraxQuery] (monitorSolarProduction), which
// answers a GraphQL request synchronously, in this process. Every mutation is
// Queue-only, so dispatched work always lands on a worker.
//
// GraphQL schema (generated from the train attributes):
//   Queries:       discover { solar { monitorSolarProduction } }   anonymous
//   Mutations:     dispatch { tradeGridEnergy, optimizeMicrogrid, processChargingSession,
//                  battery { manageBatteryStorage },
//                  sustainability { generateSustainabilityReport } }   Operator role, queue only
//   Operations:    operations { manifests, executions, ... }        Operator role
//   Subscriptions: onTrainStarted, onTrainCompleted, onTrainFailed, ...
//
// Run it (from Trax.Samples/):
//   1. docker compose up -d        Postgres on 5432, RabbitMQ on 5672 (user trax / trax123)
//   2. dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Hub
//   3. dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Worker
//
// Endpoints (Development):
//   Dashboard:   http://localhost:5202/trax
//   GraphQL IDE: http://localhost:5202/trax/graphql
//
// Try it:
//   # Live solar read, answered by the hub itself (anonymous)
//   curl -s http://localhost:5202/trax/graphql -H "Content-Type: application/json" \
//     -d '{"query":"{ discover { solar { monitorSolarProduction(input: {arrayId: \"SPA-001\", region: \"somerset\"}) { arrayId totalKwh efficiency } } } }"}'
//
//   # Queue a grid trade; the worker runs it (Operator key, Development only)
//   curl -s http://localhost:5202/trax/graphql -H "Content-Type: application/json" \
//     -H "X-Api-Key: energyhub-operator-key-do-not-use-in-production" \
//     -d '{"query":"mutation { dispatch { tradeGridEnergy(input: {ratePerKwh: 0.14, maxSellPercent: 80}) { externalId workQueueId } } }"}'
//
//   # Read the scheduler's manifests (Operator key)
//   curl -s http://localhost:5202/trax/graphql -H "Content-Type: application/json" \
//     -H "X-Api-Key: energyhub-operator-key-do-not-use-in-production" \
//     -d '{"query":"{ operations { manifests(take: 5) { items { externalId scheduleType } } } }"}'
//
// Docs: https://traxsharp.net/docs/samples/energy-hub
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.EnergyHub;
using Trax.Samples.EnergyHub.Hub;
using Trax.Samples.EnergyHub.Trains.BatteryStorage.ManageBatteryStorage;
using Trax.Samples.EnergyHub.Trains.ChargingSessions.ProcessChargingSession;
using Trax.Samples.EnergyHub.Trains.GridTrading.TradeGridEnergy;
using Trax.Samples.EnergyHub.Trains.Microgrid.OptimizeMicrogrid;
using Trax.Samples.EnergyHub.Trains.SolarProduction.MonitorSolarProduction;
using Trax.Samples.EnergyHub.Trains.Sustainability.GenerateSustainabilityReport;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

var rabbitMqConnectionString =
    builder.Configuration.GetConnectionString("RabbitMQ")
    ?? throw new InvalidOperationException("Connection string 'RabbitMQ' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Who may operate the hub ─────────────────────────────────────────────
// Mutations and the operations namespace require the Operator role. The only key that carries it
// is a published demo key, registered in Development alone (`dotnet run` starts in Development
// through Properties/launchSettings.json). Anywhere else no key is registered, so those surfaces
// refuse every caller until you register real credentials.
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoKeys.OperatorKey, id: "operator", EnergyHubRoles.Operator)
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddDataContextLogging()
                .AddJson()
                .SaveTrainParameters()
                .AddJunctionProgress()
                .UseBroadcaster(b =>
                    b.UseRabbitMq(
                        rabbitMqConnectionString,
                        o => o.ExchangeName = LifecycleEvents.ExchangeName
                    )
                )
        )
        .AddMediator(typeof(ManifestNames).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                // ── Schedule here, execute on the workers ─────────────────────
                // Without this line a scheduler on Postgres registers PostgresJobSubmitter
                // AND starts LocalWorkerService, so the hub would run jobs itself. Naming
                // the submitter registers it alone: jobs are written to background_job and
                // wait there until a Worker process claims them.
                .OverrideSubmitter(services =>
                    services.AddScoped<IJobSubmitter, PostgresJobSubmitter>()
                )
                .AddMetadataCleanup(cleanup =>
                {
                    cleanup.AddTrainType<IMonitorSolarProductionTrain>();
                    cleanup.AddTrainType<IManageBatteryStorageTrain>();
                    cleanup.AddTrainType<IProcessChargingSessionTrain>();
                    cleanup.AddTrainType<IOptimizeMicrogridTrain>();
                    cleanup.AddTrainType<ITradeGridEnergyTrain>();
                    cleanup.AddTrainType<IGenerateSustainabilityReportTrain>();
                })
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                // 1. INTERVAL + DEPENDENCY CHAIN + VARIANCE
                //    Monitor solar PV array output every 5 minutes with up to
                //    1 minute of jitter to stagger sensor reads across arrays.
                //    Battery storage management triggers after each solar read.
                //
                //    monitor-solar-production (every 5 min ± 1 min)
                //      └── manage-battery-storage (ThenInclude — depends on solar)
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .Schedule<IMonitorSolarProductionTrain>(
                    ManifestNames.MonitorSolarProduction,
                    new MonitorSolarProductionInput { ArrayId = "SPA-001", Region = "somerset" },
                    Every.Minutes(5).WithVariance(TimeSpan.FromMinutes(1))
                )
                .ThenInclude<IManageBatteryStorageTrain>(
                    ManifestNames.ManageBatteryStorage,
                    new ManageBatteryStorageInput
                    {
                        BatteryBankId = "BAT-001",
                        TargetChargePercent = 80,
                    }
                )
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                // 2. BATCH SCHEDULING — EV CHARGING PER ZONE + VARIANCE
                //    Process charging sessions per zone (plaza, data-center, parking)
                //    every 2 minutes with up to 30s of jitter to stagger zone polling.
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .ScheduleMany<IProcessChargingSessionTrain>(
                    ManifestNames.ProcessChargingSession,
                    ManifestNames.Zones.Select(zone => new ManifestItem(
                        zone,
                        new ProcessChargingSessionInput
                        {
                            StationId = $"EVC-{zone.ToUpperInvariant()}",
                            SessionType = zone == "parking" ? "Wireless" : "Wired",
                        }
                    )),
                    Every.Minutes(2).WithVariance(TimeSpan.FromSeconds(30)),
                    o => o.Group(group => group.MaxActiveJobs(3))
                )
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                // 3. INTERVAL — MICROGRID OPTIMIZATION
                //    Optimize energy distribution across the microgrid every 15 min.
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .Schedule<IOptimizeMicrogridTrain>(
                    ManifestNames.OptimizeMicrogrid,
                    new OptimizeMicrogridInput { GridZone = "somerset-hub" },
                    Every.Minutes(15)
                )
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                // 4. CRON — HOURLY GRID ENERGY TRADING
                //    Sell excess energy back to the grid via PTC UBOSS every hour.
                //    Rate: $0.14/kWh, up to 80% of battery can be sold.
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .Schedule<ITradeGridEnergyTrain>(
                    ManifestNames.TradeGridEnergy,
                    new TradeGridEnergyInput { RatePerKwh = 0.14m, MaxSellPercent = 80 },
                    Cron.Hourly()
                )
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                // 5. CRON — DAILY SUSTAINABILITY REPORT
                //    Generate a sustainability report at midnight aggregating all
                //    energy hub metrics: carbon offset, renewable %, revenue.
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .Schedule<IGenerateSustainabilityReportTrain>(
                    ManifestNames.GenerateSustainabilityReport,
                    new GenerateSustainabilityReportInput { ReportPeriod = "Daily" },
                    Cron.Daily(hour: 0)
                )
        )
);

// ── Dashboard: Development only ────────────────────────────────────────
// The dashboard can queue, run and cancel trains and change scheduler settings. This sample puts
// no login in front of it, so it is served only in Development, where it is open to anyone who
// can reach localhost. Gate it (RequirePolicy / RequireRoles) before serving it anywhere else.
if (builder.Environment.IsDevelopment())
    builder.AddTraxDashboard(dashboard => dashboard.AllowAnonymousDashboard());

// ── Register GraphQL API ────────────────────────────────────────────────
// Trains annotated with [TraxQuery] or [TraxMutation] get typed GraphQL
// fields generated. [TraxBroadcast] trains emit subscription events, and
// because UseBroadcaster is registered, AddTraxGraphQL also forwards the
// events workers publish over RabbitMQ to this hub's subscribers.

// Depth 6 accommodates the dispatch → mutation → output → nested type →
// field → scalar query chain. The Trax default of 4 is the conservative
// production choice; raise it deliberately when your schema needs it.
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .MaxExecutionDepth(6)
        // The operations namespace lists manifests and executions and triggers, disables and
        // cancels scheduled work. Both halves are off by default; exposing them requires a gate.
        // GateOperations gates that namespace alone, so the anonymous solar query keeps working.
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: EnergyHubRoles.Operator)
);
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
    app.UseTraxDashboard();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();

namespace Trax.Samples.EnergyHub.Hub
{
    public partial class Program;
}
