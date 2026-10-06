// ---------------------------------------------------------------------------
// Trax GraphQL API
//
// Serves your trains as GraphQL queries and mutations. Runs with no database:
// the in-memory provider keeps every run in this process and loses it on restart.
// README.md says how to switch to Postgres, add a train and run the tests;
// https://traxsharp.net/docs/reference/templates describes every file.
//
// Try it (Development, which `dotnet run` starts through launchSettings.json):
//   dotnet run
//   http://localhost:5402/trax/graphql   the GraphQL IDE
//   http://localhost:5402/trax/health    the health check
//
//   Send the header X-Api-Key: demo-key-do-not-use-in-production with every operation:
//     query    { discover { lookup(input: { id: "42" }) { id name createdAt } } }
//     mutation { dispatch { helloWorld(input: { name: "Trax" }) { externalId metadataId } } }
//
// This host has no scheduler, so it runs every mutation itself. Queueing work for a
// separate scheduler process (mode: QUEUE) needs a database both processes share; use
// trax-hub for API and scheduler in one process.
//
// Outside Development there is no demo key, so every operation is refused until you
// add real credentials. See "Before deploying" in README.md.
// ---------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Api.Auth;
using Trax.Samples.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// -- 1. Authentication and authorization --------------------------------------
// Every train and query model in this project carries [TraxAuthorize(Roles = "User")], so
// a caller needs a credential holding the User role. The demo key holds it, and it exists
// only in Development: Trax.Api refuses to start a host outside Development with a key
// containing "do-not-use-in-production" registered. Anywhere else, register real
// credentials (AddHashed keys from a secret store, or AddTraxJwtAuth) before removing the
// IsDevelopment() check: https://traxsharp.net/docs/api-security.
// AddAuthentication() stays outside the check. AddTraxApiKeyAuth registers authentication
// itself, so without this line a start outside Development fails in UseAuthentication()
// with "Unable to resolve service for type IAuthenticationSchemeProvider". AddAuthorization()
// is where your own policies go: AddAuthorization(o => o.AddPolicy(...)).
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys => keys.Add(DemoKeys.DemoKey, id: "demo", "User"));
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// -- 2. Trax: the effect system and the mediator --------------------------------
// AddTrax comes before AddTraxGraphQL, which throws without it.
//   UseInMemory()          where runs are recorded. Swap for UsePostgres(connectionString)
//                          (package Trax.Effect.Data.Postgres) to keep them across restarts.
//   SaveTrainParameters()  stores each run's input and output with its record.
//   AddMediator(...)       registers every train in the assemblies named, under its
//                          interface. A train in an assembly not named here does not exist,
//                          and its GraphQL field is missing.
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
        .AddMediator(typeof(Program).Assembly)
);

// -- 3. Your application's own data -------------------------------------------
// A plain EF Core context, separate from Trax's own tables. Swap UseInMemoryDatabase for
// UseNpgsql(connectionString) (package Npgsql.EntityFrameworkCore.PostgreSQL) to store it in
// Postgres under its own schema (Data/AppSchema.cs).
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseInMemoryDatabase("app"));
builder.Services.AddScoped<IAppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext()
);

// -- 4. GraphQL -----------------------------------------------------------------
// Builds the schema from the trains AddMediator registered: [TraxQuery] trains under
// query { discover }, [TraxMutation] trains under mutation { dispatch }, and the
// [TraxQueryModel] entities of AppDbContext under query { discover { app } }. It refuses to
// start when an exposed train or query model carries neither [TraxAuthorize] nor
// [TraxAllowAnonymous], and when the schema would have no query at all.
builder.Services.AddTraxGraphQL(graphql => graphql.AddDbContext<AppDbContext>());
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

// -- Create the application tables (demo bootstrap) ----------------------------
// Fine for the in-memory database. With Postgres, use EF Core migrations instead.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
}

// -- Middleware and endpoints ------------------------------------------------
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();
