// ─────────────────────────────────────────────────────────────────────────────
// Trax Scheduler with Dashboard
//
// Runs scheduled trains on a configurable interval. Includes a Blazor
// dashboard for monitoring at /trax, in Development only. Uses an in-memory data provider by
// default so you can run it immediately without any external dependencies.
//
// To switch providers, replace UseInMemory() with UseSqlite(connectionString) or
// UsePostgres(connectionString) and add the corresponding Trax.Effect.Data package.
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
// The dashboard can queue, run and cancel trains and change scheduler settings, and this
// template puts no authorization in front of it, so it is served only in Development, where
// `dotnet run` starts (see Properties/launchSettings.json). Gate it before serving it anywhere
// else: see https://traxsharp.net/docs/dashboard.
if (builder.Environment.IsDevelopment())
    builder.AddTraxDashboard();

var app = builder.Build();

// ── Map dashboard ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
    app.UseTraxDashboard();

app.Run();
