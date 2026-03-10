// ─────────────────────────────────────────────────────────────────────────────
// Trax Scheduler with Dashboard
//
// Runs scheduled trains on a configurable interval using PostgreSQL as the
// task server. Includes a Blazor dashboard for monitoring at /trax.
//
// Prerequisites:
//   1. Start PostgreSQL (e.g. docker compose up -d)
//   2. Run this project: dotnet run
//
// Try it:
//   Open http://localhost:5001/trax in a browser for the Trax Dashboard
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StepProvider.Progress.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Scheduler.Trains.HelloWorld;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Dashboard ───────────────────────────────────────────────────────────
builder.AddTraxDashboard();

// ── Register Trax Effect + Scheduler ────────────────────────────────────
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects.UsePostgres(connectionString).AddJson().SaveTrainParameters().AddStepProgress()
        )
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                .UseLocalWorkers()
                // Schedule the HelloWorld train to run every 20 seconds.
                // Replace this with your own trains and schedules.
                .Schedule<IHelloWorldTrain>(
                    "hello-world",
                    new HelloWorldInput { Name = "Trax" },
                    Every.Seconds(20)
                )
        )
);

var app = builder.Build();

// ── Map dashboard ───────────────────────────────────────────────────────
app.UseTraxDashboard();

app.Run();
