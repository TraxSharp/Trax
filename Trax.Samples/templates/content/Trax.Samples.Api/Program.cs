// ─────────────────────────────────────────────────────────────────────────────
// Trax GraphQL API
//
// A GraphQL API powered by HotChocolate. Handles lightweight operations
// directly via mutations and can queue heavy work for a separate scheduler
// process by passing mode: QUEUE. Uses an in-memory data provider by default
// so you can run it immediately without any external dependencies.
//
// To switch to PostgreSQL, replace UseInMemory() with UsePostgres(connectionString)
// and swap the Trax.Effect.Data.InMemory package for Trax.Effect.Data.Postgres.
//
// Try it:
//   dotnet run
//   Open http://localhost:5002/trax/graphql in a browser for Banana Cake Pop IDE
//
//   # Query a train directly (typed query from [TraxQuery])
//   query { discover { lookup(input: { id: "42" }) { id name createdAt } } }
//
//   # Run a mutation (from [TraxMutation])
//   mutation { dispatch { helloWorld(input: { name: "Trax" }) { externalId metadataId } } }
//
//   # Health check
//   curl http://localhost:5002/trax/health
//
// Third-party packages used by this project (via Trax dependencies):
//   HotChocolate    — GraphQL server (MIT, https://github.com/ChilliCream/graphql-platform)
//   LanguageExt     — Functional programming primitives (MIT, https://github.com/louthy/language-ext)
//   EF Core InMemory — In-memory database provider (MIT, https://github.com/dotnet/efcore)
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Register Trax Effect + Mediator ─────────────────────────────────────
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UseInMemory()).AddMediator(typeof(Program).Assembly)
);

// ── Register GraphQL API ────────────────────────────────────────────────
builder.Services.AddAuthorization();
builder.Services.AddTraxGraphQL();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

// ── Map endpoints ───────────────────────────────────────────────────────
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();
