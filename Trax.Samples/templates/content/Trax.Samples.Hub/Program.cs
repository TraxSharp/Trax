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
//   Open http://localhost:5000/trax for the Dashboard
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

using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Hub.Trains.HelloWorld;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(logging => logging.AddConsole());

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

// -- Dashboard ---------------------------------------------------------------
builder.AddTraxDashboard();

// -- GraphQL API -------------------------------------------------------------
builder.Services.AddAuthorization();
builder.Services.AddTraxGraphQL();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

// -- Map endpoints -----------------------------------------------------------
app.UseTraxDashboard();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();
