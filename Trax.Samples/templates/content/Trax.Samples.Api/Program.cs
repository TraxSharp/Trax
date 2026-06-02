// ─────────────────────────────────────────────────────────────────────────────
// Trax GraphQL API
//
// A GraphQL API powered by HotChocolate. Handles lightweight operations
// directly via mutations and can queue heavy work for a separate scheduler
// process by passing mode: QUEUE. Uses an in-memory data provider by default
// so you can run it immediately without any external dependencies.
//
// To switch providers, replace UseInMemory() with UseSqlite(connectionString) or
// UsePostgres(connectionString) and add the corresponding Trax.Effect.Data package.
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

using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Api.Auth;
using Trax.Samples.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Authentication (NO WARRANTY, demo key only) ─────────────────────────
// Send the key as the X-Api-Key header. Per-operation gates use [TraxAuthorize].
builder.Services.AddTraxApiKeyAuth(keys => keys.Add(DemoKeys.DemoKey, id: "demo", "User"));
builder.Services.AddAuthorization();

// ── Register Trax Effect + Mediator ─────────────────────────────────────
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UseInMemory()).AddMediator(typeof(Program).Assembly)
);

// ── Register the application data context (one project : one schema : one context) ──
// Swap UseInMemoryDatabase for UseNpgsql(connectionString) (and add Trax.Effect.Data.Postgres)
// to get real schema isolation.
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseInMemoryDatabase("app"));
builder.Services.AddScoped<IAppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext()
);

// ── Register GraphQL API and expose the data context's query models ─────
builder.Services.AddTraxGraphQL(graphql => graphql.AddDbContext<AppDbContext>());
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

// ── Ensure the application tables exist (demo bootstrap) ────────────────
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
}

// ── Map endpoints ───────────────────────────────────────────────────────
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();
