// ─────────────────────────────────────────────────────────────────────────────
// Trax Scheduler with Dashboard
//
// Runs scheduled trains on a configurable interval. Includes a Blazor
// dashboard for monitoring at /trax. Uses an in-memory data provider by
// default so you can run it immediately without any external dependencies.
//
// To switch to PostgreSQL, replace UseInMemory() with UsePostgres(connectionString)
// and swap the Trax.Effect.Data.InMemory package for Trax.Effect.Data.Postgres.
//
// Try it:
//   dotnet run
//   Open http://localhost:5001/trax in a browser for the Trax Dashboard
//
// Third-party packages used by this project (via Trax dependencies):
//   Radzen.Blazor   — Dashboard UI components (MIT, https://github.com/radzenhq/radzen-blazor)
//   LanguageExt     — Functional programming primitives (MIT, https://github.com/louthy/language-ext)
//   Cronos          — Cron expression parser (MIT, https://github.com/HangfireIO/Cronos)
//   EF Core InMemory — In-memory database provider (MIT, https://github.com/dotnet/efcore)
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Dashboard.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Scheduler.Trains.HelloWorld;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Register Trax Effect + Scheduler ────────────────────────────────────
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UseInMemory())
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler
            // Schedule the HelloWorld train to run every 20 seconds.
            // Replace this with your own trains and schedules.
            .Schedule<IHelloWorldTrain>(
                "hello-world",
                new HelloWorldInput { Name = "Trax" },
                Every.Seconds(20)
            )
        )
);

// ── Dashboard ───────────────────────────────────────────────────────────
builder.AddTraxDashboard();

var app = builder.Build();

// ── Map dashboard ───────────────────────────────────────────────────────
app.UseTraxDashboard();

app.Run();
