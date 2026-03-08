// ─────────────────────────────────────────────────────────────────────────────
// Somerset Energy Hub — Standalone Worker (execution only, no scheduling)
//
// Polls the background_job table and executes energy hub trains: solar
// monitoring, battery management, EV charging processing, microgrid
// optimization, grid trading, and sustainability reporting.
//
// This process has no ManifestManager, no JobDispatcher, and no scheduling
// logic — it only runs jobs that have already been dispatched by the Hub.
//
// This demonstrates Model #3 (Standalone Workers): the hub (GraphQL API +
// scheduler + dashboard) and workers run as independent processes, connected
// only through PostgreSQL. You can scale this worker horizontally by running
// multiple instances.
//
// Prerequisites:
//   1. Start Postgres:  cd Trax.Samples && docker compose up -d
//   2. Pack local:      ./pack-local.sh
//   3. Start hub:       dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Hub
//   4. Start worker:    dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Worker
//
// The worker picks up jobs atomically using PostgreSQL's FOR UPDATE SKIP LOCKED,
// so multiple worker instances can run safely without duplicate execution.
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StepProvider.Progress.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.EnergyHub;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

var rabbitMqConnectionString =
    builder.Configuration.GetConnectionString("RabbitMQ")
    ?? throw new InvalidOperationException("Connection string 'RabbitMQ' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Register Trax Effect + Mediator (trains, bus, discovery, execution) ──
// The worker must reference the same train assemblies as the scheduler so it
// can resolve and execute any train type that gets dispatched.
// UseBroadcaster() publishes lifecycle events to RabbitMQ so the Hub's
// GraphQL subscriptions are notified when queued trains complete.
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddJson()
                .SaveTrainParameters()
                .AddStepProgress()
                .UseBroadcaster(b => b.UseRabbitMq(rabbitMqConnectionString))
        )
        .AddMediator(typeof(ManifestNames).Assembly)
);

// ── Register standalone worker ───────────────────────────────────────────
// AddTraxWorker registers the job execution pipeline (JobRunnerTrain) and
// LocalWorkerService as a hosted service that polls background_job.
builder.Services.AddTraxWorker(opts =>
{
    opts.WorkerCount = 4;
    opts.PollingInterval = TimeSpan.FromSeconds(1);
});

var app = builder.Build();

app.Run();
