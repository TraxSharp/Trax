// ---------------------------------------------------------------------------
// Trax Hub: GraphQL API + Scheduler + Dashboard in one process
//
// Runs with no database: the in-memory provider keeps every run in this process
// and loses it on restart. README.md says how to switch to Postgres, add a
// train and run the tests; https://traxsharp.net/docs/reference/templates
// describes every file.
//
// Try it (Development, which `dotnet run` starts through launchSettings.json):
//   dotnet run
//   http://localhost:5400/trax/graphql   the GraphQL IDE
//   http://localhost:5400/trax           the dashboard
//   http://localhost:5400/trax/health    the health check
//
//   Send the header X-Api-Key: demo-key-do-not-use-in-production with every operation:
//     query    { discover { lookup(input: { id: "42" }) { id name createdAt } } }
//     mutation { dispatch { helloWorld(input: { name: "Trax" }) { externalId metadataId } } }
//
// Outside Development there is no demo key and no dashboard, so every operation is
// refused and /trax is a 404 until you add real credentials. See "Before deploying" in
// README.md.
// ---------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Hub.Auth;
using Trax.Samples.Hub.Data;
using Trax.Samples.Hub.Trains.HelloWorld;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

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

// -- 2. Trax: the effect system, the mediator and the scheduler -----------------
// AddTrax comes before AddTraxGraphQL and AddTraxDashboard, which throw without it.
//   UseInMemory()          where runs are recorded. Swap for UsePostgres(connectionString)
//                          (package Trax.Effect.Data.Postgres) to keep them across restarts
//                          and share them between processes.
//   SaveTrainParameters()  stores each run's input and output, which the dashboard shows.
//   AddJunctionProgress()  records the running junction and lets the dashboard cancel a run
//                          between junctions.
//   AddMediator(...)       registers every train in the assemblies named, under its
//                          interface. A train in an assembly not named here does not exist:
//                          its GraphQL field is missing and Schedule<T> refuses to start.
//   AddScheduler(...)      runs manifests. The dashboard needs it: UseTraxDashboard() refuses
//                          to start without it.
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters().AddJunctionProgress())
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler.Schedule<IHelloWorldTrain>(
                "hello-world",
                new HelloWorldInput { Name = "Trax" },
                Every.Seconds(20)
            )
        )
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

// -- 5. Dashboard (Development only) ------------------------------------------
// The dashboard can queue, run and cancel trains and change scheduler settings.
// UseTraxDashboard() refuses to start until AddTraxDashboard says who may use it.
// AllowAnonymousDashboard() lets anyone who can reach the port use it, so it is declared
// only here, and the dashboard is not served outside Development. To serve it elsewhere,
// choose RequirePolicy("<policy>") or RequireRoles("<role>") instead and drop the
// IsDevelopment() checks: https://traxsharp.net/docs/dashboard.
if (builder.Environment.IsDevelopment())
    builder.AddTraxDashboard(dashboard => dashboard.AllowAnonymousDashboard());

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
if (app.Environment.IsDevelopment())
    app.UseTraxDashboard();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();
