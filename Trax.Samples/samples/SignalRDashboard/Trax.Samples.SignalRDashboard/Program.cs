// ─────────────────────────────────────────────────────────────────────────────
// Trax SignalR Dashboard — Blazor Server E2E sample
//
// A single-process Blazor Server app that exercises the Trax SignalR
// broadcaster sink end-to-end:
//
//   - schedules a PingTrain every 10 seconds via the scheduler
//   - executes it locally via AddTraxWorker()
//   - uses UseBroadcaster(b => b.UseSignalRHub()) to push lifecycle events
//     to connected SignalR clients
//   - maps the hub at /hubs/trax-events via MapTraxTrainEventHub()
//   - serves a Blazor Server page at /events that subscribes to the hub
//     and renders a live, rolling list of train lifecycle events
//
// Prerequisites:
//   1. Start Postgres:  cd Trax.Samples && docker compose up -d
//   2. Pack local:      ./pack-local.sh
//   3. Run:             dotnet run --project samples/SignalRDashboard/Trax.Samples.SignalRDashboard
//
// Endpoints:
//   Live events page:    http://localhost:5210/events
//   Trax dashboard:      http://localhost:5210/trax
//   SignalR hub:         /hubs/trax-events
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Dashboard.Extensions;
using Trax.Effect.Broadcaster.SignalR.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.SignalRDashboard;
using Trax.Samples.SignalRDashboard.Components;
using Trax.Samples.SignalRDashboard.Trains.Ping;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

// Blazor Server + SignalR (SignalR is required by both Blazor itself and the Trax sink).
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSignalR();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddJson()
                .AddJunctionProgress()
                .UseBroadcaster(b =>
                    b.UseSignalRHub(opts =>
                        opts.OnlyForEvents("Started", "Completed", "Failed", "Cancelled")
                    )
                )
        )
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler.Schedule<IPingTrain>(
                "ping",
                new PingInput { Source = "scheduled" },
                Every.Seconds(10)
            )
        )
);

// The scheduler with Postgres already registers LocalWorkerService for in-process
// execution by default, so no separate AddTraxWorker() call is needed.

builder.AddTraxDashboard();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.UseTraxDashboard();

app.MapTraxTrainEventHub();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

namespace Trax.Samples.SignalRDashboard
{
    public partial class Program;
}
