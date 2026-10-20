// ---------------------------------------------------------------------------
// Trax Hub: GraphQL API + Scheduler + operations console in one process
//
// Runs with no database: the in-memory provider keeps every run in this process
// and loses it on restart. README.md says how to switch to Postgres, add a
// train and run the tests; https://traxsharp.net/docs/reference/templates
// describes every file.
//
// Try it (Development, which `dotnet run` starts through launchSettings.json):
//   dotnet run
//   http://localhost:5400/trax/graphql   the GraphQL IDE
//   http://localhost:5400/trax/dashboard the operations console (paste the operator key)
//   http://localhost:5400/trax/health    the health check
//
//   Send the header X-Api-Key: demo-key-do-not-use-in-production with every operation:
//     query    { discover { lookup(input: { id: "42" }) { id name createdAt } } }
//     mutation { dispatch { helloWorld(input: { name: "Trax" }) { externalId metadataId } } }
//   The console, and query { operations { ... } }, take the operator key instead:
//     X-Api-Key: operator-key-do-not-use-in-production
//
// Outside Development there are no demo keys and no console, so every operation is
// refused and /trax/dashboard is a 404 until you add real credentials. See "Before
// deploying" in README.md.
// ---------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
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
// a caller needs a credential holding the User role. The demo key holds it. The operations
// namespace, which the console reads and writes, needs the Operator role, which only the
// operator key holds. Both exist only in Development: Trax.Api refuses to start a host outside
// Development with a key containing "do-not-use-in-production" registered. Anywhere else, register real
// credentials (AddHashed keys from a secret store, or AddTraxJwtAuth) before removing the
// IsDevelopment() check: https://traxsharp.net/docs/security/secure-a-graphql-host.
// AddAuthentication() stays outside the check. AddTraxApiKeyAuth registers authentication
// itself, so without this line a start outside Development fails in UseAuthentication()
// with "Unable to resolve service for type IAuthenticationSchemeProvider". AddAuthorization()
// is where your own policies go: AddAuthorization(o => o.AddPolicy(...)).
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoKeys.DemoKey, id: "demo", "User")
            .Add(DemoKeys.OperatorKey, id: "operator", DemoKeys.OperatorRole)
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// -- 2. Trax: the effect system, the mediator and the scheduler -----------------
// AddTrax comes before AddTraxGraphQL, which throws without it.
//   UseInMemory()          where runs are recorded. Swap for UsePostgres(connectionString)
//                          (package Trax.Effect.Data.Postgres) to keep them across restarts
//                          and share them between processes.
//   SaveTrainParameters()  stores each run's input and output, which the console shows.
//   AddJunctionProgress()  records the running junction and lets the console cancel a run
//                          between junctions.
//   AddMediator(...)       registers every train in the assemblies named, under its
//                          interface. A train in an assembly not named here does not exist:
//                          its GraphQL field is missing and Schedule<T> refuses to start.
//   AddScheduler(...)      runs manifests. The console needs it: the operations namespace
//                          reads the scheduler's services.
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
// The operations namespace (runs, the work queue, dead letters, schedules, and the actions on
// them) is off until exposed, and exposing it without a gate refuses to start. The console
// needs both halves; GateOperations puts the Operator role in front of the namespace alone.
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .AddDbContext<AppDbContext>()
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: DemoKeys.OperatorRole)
);
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

// -- Operations console (Development only) ---------------------------------------
// The console can queue, run and cancel trains and change scheduler settings, all through the
// operations namespace, so each request it sends needs the operator key, which it asks for on
// every load. UseTraxApiDashboard() refuses to start until it is told who may load the page.
// AllowAnonymousShell() puts no sign-in in front of the page, so it is declared only here, and
// the console is not served outside Development. To serve it elsewhere, add a sign-in the
// browser carries (a cookie or OpenID Connect), choose RequirePolicy("<policy>") or
// RequireRoles("<role>") instead, and drop the IsDevelopment() check:
// https://traxsharp.net/docs/sdk-reference/api-dashboard/use-trax-api-dashboard.
if (app.Environment.IsDevelopment())
    app.UseTraxApiDashboard(dashboard => dashboard.AllowAnonymousShell());

app.Run();
