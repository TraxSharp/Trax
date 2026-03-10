// ─────────────────────────────────────────────────────────────────────────────
// Somerset Energy Hub — Combined Hub (GraphQL API + Scheduler + Dashboard)
//
// A single process that serves the GraphQL API, manages scheduling, and hosts
// the Trax dashboard — but does NOT execute trains. All scheduled and queued
// jobs are written to the background_job table via PostgresJobSubmitter. A
// separate Worker process polls the table and runs the trains.
//
// This demonstrates Model #3 (Standalone Workers) with a combined API surface:
// operators can query live solar/battery data, queue ad-hoc jobs via GraphQL,
// AND rely on automatic scheduling — all from one lightweight process. The
// heavy lifting is offloaded to the Worker.
//
// GraphQL schema (auto-generated from train attributes):
//   Queries:    monitorSolarProduction  — [TraxQuery]  (live sensor read)
//   Mutations:  queueManageBatteryStorage, runProcessChargingSession,
//               queueProcessChargingSession, queueOptimizeMicrogrid,
//               queueTradeGridEnergy, runGenerateSustainabilityReport,
//               queueGenerateSustainabilityReport
//   Subscriptions: onTrainStarted, onTrainCompleted, onTrainFailed
//
// Prerequisites:
//   1. Start Postgres:  cd Trax.Samples && docker compose up -d
//   2. Pack local:      ./pack-local.sh
//   3. Start hub:       dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Hub
//   4. Start worker:    dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Worker
//
// Endpoints:
//   Dashboard:   http://localhost:5202/trax
//   GraphQL IDE: http://localhost:5202/trax/graphql  (Banana Cake Pop)
//
// Try it:
//   # Query live solar production
//   curl -X POST http://localhost:5202/trax/graphql \
//        -H "Content-Type: application/json" \
//        -d '{"query":"{ discover { monitorSolarProduction(input: {arrayId: \"SPA-001\", region: \"somerset\"}) { currentOutputKw peakOutputKw efficiencyPercent } } }"}'
//
//   # Queue a grid energy trade
//   curl -X POST http://localhost:5202/trax/graphql \
//        -H "Content-Type: application/json" \
//        -d '{"query":"mutation { dispatch { queueTradeGridEnergy(input: {ratePerKwh: 0.14, maxSellPercent: 80}) { workQueueId externalId } } }"}'
//
//   # Generate a sustainability report (runs synchronously on the hub)
//   curl -X POST http://localhost:5202/trax/graphql \
//        -H "Content-Type: application/json" \
//        -d '{"query":"mutation { dispatch { runGenerateSustainabilityReport(input: {reportPeriod: \"Daily\"}) { carbonOffsetKg renewablePercent totalGenerationKwh revenueUsd } } }"}'
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StepProvider.Progress.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.EnergyHub;
using Trax.Samples.EnergyHub.Trains.BatteryStorage.ManageBatteryStorage;
using Trax.Samples.EnergyHub.Trains.ChargingSessions.ProcessChargingSession;
using Trax.Samples.EnergyHub.Trains.GridTrading.TradeGridEnergy;
using Trax.Samples.EnergyHub.Trains.Microgrid.OptimizeMicrogrid;
using Trax.Samples.EnergyHub.Trains.SolarProduction.MonitorSolarProduction;
using Trax.Samples.EnergyHub.Trains.Sustainability.GenerateSustainabilityReport;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

var rabbitMqConnectionString =
    builder.Configuration.GetConnectionString("RabbitMQ")
    ?? throw new InvalidOperationException("Connection string 'RabbitMQ' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddDataContextLogging()
                .AddJson()
                .SaveTrainParameters()
                .AddStepProgress()
                .UseBroadcaster(b => b.UseRabbitMq(rabbitMqConnectionString))
        )
        .AddMediator(typeof(ManifestNames).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                // ── Key: scheduling only, no local execution ──────────────────
                // PostgresJobSubmitter is the default — omit UseLocalWorkers() to schedule without executing locally
                // Jobs accumulate until the Worker process picks them up.
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
                // 1. INTERVAL + DEPENDENCY CHAIN
                //    Monitor solar PV array output every 5 minutes, then manage
                //    battery storage based on the latest solar production data.
                //
                //    monitor-solar-production (every 5 min)
                //      └── manage-battery-storage (ThenInclude — depends on solar)
                // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                .Schedule<IMonitorSolarProductionTrain>(
                    ManifestNames.MonitorSolarProduction,
                    new MonitorSolarProductionInput { ArrayId = "SPA-001", Region = "somerset" },
                    Every.Minutes(5)
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
                // 2. BATCH SCHEDULING — EV CHARGING PER ZONE
                //    Process charging sessions per zone (plaza, data-center, parking)
                //    every 2 minutes. Each zone has its own manifest.
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
                    Every.Minutes(2),
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

// ── Register GraphQL API ────────────────────────────────────────────────
// Trains annotated with [TraxQuery] or [TraxMutation] get typed GraphQL
// fields auto-generated. [TraxBroadcast] trains emit subscription events.
builder.AddTraxDashboard();
builder.Services.AddTraxGraphQL();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

app.UseTraxDashboard();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();
