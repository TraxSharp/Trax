// ---------------------------------------------------------------------------
// Trax Scheduler with the operations console
//
// Runs the HelloWorld train every 20 seconds and shows each run in the operations console.
// Runs with no database: the in-memory provider keeps every run in this process
// and loses it on restart. README.md says how to switch to Postgres, add a train
// and run the tests; https://traxsharp.net/docs/reference/templates describes
// every file.
//
// Try it (Development, which `dotnet run` starts through launchSettings.json):
//   dotnet run
//   http://localhost:5401/trax/dashboard   the operations console
//   Paste the operator key when it asks: operator-key-do-not-use-in-production
//
// Outside Development there is no demo key and the console is not served
// (/trax/dashboard is a 404) until you choose who may use it. See "Before deploying" in
// README.md.
// ---------------------------------------------------------------------------

using Trax.Api.Auth.ApiKey;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Scheduler.Auth;
using Trax.Samples.Scheduler.Trains.HelloWorld;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

// -- 1. Authentication and authorization --------------------------------------
// The console reads and changes everything through the GraphQL operations namespace, which
// needs the Operator role. The demo key holds it, and it exists only in Development: Trax.Api
// refuses to start a host outside Development with a key containing
// "do-not-use-in-production" registered. Anywhere else, register real credentials (AddHashed
// keys from a secret store, or AddTraxJwtAuth) before removing the IsDevelopment() check:
// https://traxsharp.net/docs/api-security.
// AddAuthentication() stays outside the check. AddTraxApiKeyAuth registers authentication
// itself, so without this line a start outside Development fails in UseAuthentication().
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoKeys.OperatorKey, id: "operator", DemoKeys.OperatorRole)
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// -- 2. Trax: the effect system, the mediator and the scheduler -----------------
// AddTrax comes before AddTraxGraphQL, which throws without it.
//   UseInMemory()          where runs are recorded. Swap for UsePostgres(connectionString)
//                          (package Trax.Effect.Data.Postgres) to keep them across restarts
//                          and share the work between several scheduler processes.
//   SaveTrainParameters()  stores each run's input and output, which the console shows.
//   AddJunctionProgress()  records the running junction and lets the console cancel a run
//                          between junctions.
//   AddMediator(...)       registers every train in the assemblies named, under its
//                          interface. Schedule<T> refuses to start for a train it did not
//                          register.
//   AddScheduler(...)      stores each Schedule(...) as a manifest at startup and runs it.
//                          The console needs it: the operations namespace reads the
//                          scheduler's services.
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters().AddJunctionProgress())
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler
            // "hello-world" is the manifest's id. Schedule updates the manifest with
            // that id, so renaming it creates a new manifest rather than changing this one.
            .Schedule<IHelloWorldTrain>(
                "hello-world",
                new HelloWorldInput { Name = "Trax" },
                Every.Seconds(20)
            )
        )
);

// -- 3. GraphQL operations ------------------------------------------------------
// The operations namespace (runs, the work queue, dead letters, schedules, and the actions on
// them) is off until exposed, and exposing it without a gate refuses to start. The console
// needs both halves; GateOperations puts the Operator role in front of it.
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: DemoKeys.OperatorRole)
);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();

// -- 4. Operations console (Development only) ----------------------------------
// The console can queue, run and cancel trains and change scheduler settings. Each request it
// sends needs the operator key, which it asks for on every load. UseTraxApiDashboard() refuses
// to start until it is told who may load the page. AllowAnonymousShell() puts no sign-in in
// front of the page, so it is declared only here, and the console is not served outside
// Development. To serve it elsewhere, add a sign-in the browser carries (a cookie or OpenID
// Connect), choose RequirePolicy("<policy>") or RequireRoles("<role>") instead, and drop the
// IsDevelopment() check: https://traxsharp.net/docs/sdk-reference/api-dashboard/use-trax-api-dashboard.
if (app.Environment.IsDevelopment())
    app.UseTraxApiDashboard(dashboard => dashboard.AllowAnonymousShell());

app.Run();
