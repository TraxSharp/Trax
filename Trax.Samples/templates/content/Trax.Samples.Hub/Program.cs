// ---------------------------------------------------------------------------
// Trax Hub (API + Scheduler + Dashboard)
//
// A single process that serves a GraphQL API, runs scheduled trains, and
// hosts the Trax Dashboard. Uses an in-memory data provider by default so
// you can run it immediately without any external dependencies.
//
// To switch providers, replace UseInMemory() with UseSqlite(connectionString) or
// UsePostgres(connectionString) and add the corresponding Trax.Effect.Data package.
//
// Try it:
//   dotnet run
//   Open http://localhost:5000/trax/graphql for the GraphQL IDE
//   Open http://localhost:5000/trax for the Dashboard (Development only)
//
//   Every operation needs the demo key: send the header X-Api-Key: demo-key-do-not-use-in-production
//
//   # Query a train directly
//   query { discover { lookup(input: { id: "42" }) { id name createdAt } } }
//
//   # Run a mutation
//   mutation { dispatch { helloWorld(input: { name: "Trax" }) { externalId metadataId } } }
//
//   # Health check
//   curl http://localhost:5000/trax/health
//
// Third-party packages used by this project (via Trax dependencies):
//   HotChocolate    — GraphQL server (MIT, https://github.com/ChilliCream/graphql-platform)
//   Radzen.Blazor   — Dashboard UI components (MIT, https://github.com/radzenhq/radzen-blazor)
//   LanguageExt     — Functional programming primitives (MIT, https://github.com/louthy/language-ext)
//   Cronos          — Cron expression parser (MIT, https://github.com/HangfireIO/Cronos)
//   EF Core InMemory — In-memory database provider (MIT, https://github.com/dotnet/efcore)
// ---------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Hub.Auth;
using Trax.Samples.Hub.Data;
using Trax.Samples.Hub.Trains.HelloWorld;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(logging => logging.AddConsole());

// -- Authentication (NO WARRANTY, demo key only) -----------------------------
// Send the key as the X-Api-Key header. Every train and query model in this template carries
// [TraxAuthorize(Roles = "User")], and the demo key holds that role. The demo key is registered
// only in Development, where `dotnet run` starts (see Properties/launchSettings.json). Anywhere
// else there is no credential until you register real ones, so every operation is refused.
// Mark an operation [TraxAllowAnonymous] only when anyone on the internet may call it.
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys => keys.Add(DemoKeys.DemoKey, id: "demo", "User"));
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// -- Trax Effect + Mediator + Scheduler --------------------------------------
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UseInMemory())
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler.Schedule<IHelloWorldTrain>(
                "hello-world",
                new HelloWorldInput { Name = "Trax" },
                Every.Seconds(20)
            )
        )
);

// -- Application data context (one project : one schema : one context) -------
// Swap UseInMemoryDatabase for UseNpgsql(connectionString) (and add Trax.Effect.Data.Postgres)
// to get real schema isolation.
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseInMemoryDatabase("app"));
builder.Services.AddScoped<IAppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext()
);

// -- Dashboard ---------------------------------------------------------------
// The dashboard can queue, run and cancel trains and change scheduler settings, and this
// template puts no authorization in front of it, so it is served only in Development. Gate it
// before serving it anywhere else: see https://traxsharp.net/docs/dashboard.
if (builder.Environment.IsDevelopment())
    builder.AddTraxDashboard();

// -- GraphQL API -------------------------------------------------------------
builder.Services.AddTraxGraphQL(graphql => graphql.AddDbContext<AppDbContext>());
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

// -- Ensure the application tables exist (demo bootstrap) --------------------
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
}

// -- Map endpoints -----------------------------------------------------------
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
    app.UseTraxDashboard();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();
