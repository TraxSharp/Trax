// ─────────────────────────────────────────────────────────────────────────────
// Somerset Energy Hub: a standalone worker (execution only, no scheduling)
//
// Polls the background_job table and executes energy hub trains: solar
// monitoring, battery management, EV charging processing, microgrid
// optimization, grid trading, and sustainability reporting.
//
// This process has no ManifestManager, no JobDispatcher, no GraphQL and no
// dashboard: it only runs jobs the hub has already written to background_job.
// The hub and its workers share nothing but PostgreSQL (the jobs) and RabbitMQ
// (the lifecycle events the hub's subscriptions forward). Run as many workers
// as you like.
//
// Run it (from Trax.Samples/, after `docker compose up -d` and the hub):
//   dotnet run --project samples/DistributedWorkers/Trax.Samples.EnergyHub.Worker
//
// Docs: https://traxsharp.net/docs/reference/deployment-topologies
//
// The worker picks up jobs atomically using PostgreSQL's FOR UPDATE SKIP LOCKED,
// so multiple worker instances can run safely without duplicate execution.
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.EnergyHub;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Register Trax Effect + Mediator (trains, bus, discovery, execution) ──
// The worker must reference the same train assemblies as the scheduler so it
// can resolve and execute any train type that gets dispatched.
// UseBroadcaster() publishes lifecycle events to RabbitMQ so the hub's
// GraphQL subscriptions are notified when queued trains complete.
//
// The mutations carry [TraxAuthorize], and the mediator refuses to start a host
// with such trains and no ITrainAuthorizationService. The hub checks the caller
// when the job is queued; the worker only runs work that already passed that
// check and accepts no submissions of its own, which is the case
// AllowMissingAuthorizationService() exists for.
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddJson()
                .SaveTrainParameters()
                .AddJunctionProgress()
                .UseBroadcaster(b =>
                    // ConnectionStrings:RabbitMQ in Development; RabbitMQ:Host and its user and
                    // password where trax deploy runs the broker (BrokerTransport says why that one
                    // may be plain AMQP).
                    b.UseRabbitMq(
                        builder.Configuration,
                        o =>
                        {
                            o.ExchangeName = LifecycleEvents.ExchangeName;
                            // Signs each event, and drops one not signed with this key.
                            o.SigningKey = BroadcastSigningKey.Resolve(
                                builder.Configuration,
                                builder.Environment.IsDevelopment()
                            );
                            o.AllowInsecureTransport = BrokerTransport.AllowsCleartext(
                                builder.Configuration
                            );
                        },
                        name: BrokerTransport.Name
                    )
                )
        )
        .AddMediator(mediator =>
            mediator
                .ScanAssemblies(typeof(ManifestNames).Assembly)
                .AllowMissingAuthorizationService()
        )
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

// Serves; or, when trax deploy starts it for a step, reports what it needs (--trax-export-deploy),
// migrates (--trax-migrate) or drains and resumes the hosts (--trax-drain, --trax-resume) and exits.
await app.RunTraxAsync(args).ConfigureAwait(false);

namespace Trax.Samples.EnergyHub.Worker
{
    /// <summary>
    /// Names this assembly for <c>WebApplicationFactory&lt;Program&gt;</c> in the E2E suite. It is
    /// never created: the host is the top-level program above.
    /// </summary>
    public sealed class Program
    {
        private Program() { }
    }
}
