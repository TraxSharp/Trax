// ---------------------------------------------------------------------------
// Trax Scheduler with Dashboard
//
// Runs the HelloWorld train every 20 seconds and shows each run in the dashboard.
// Runs with no database: the in-memory provider keeps every run in this process
// and loses it on restart. README.md says how to switch to Postgres, add a train
// and run the tests; https://traxsharp.net/docs/reference/templates describes
// every file.
//
// Try it (Development, which `dotnet run` starts through launchSettings.json):
//   dotnet run
//   http://localhost:5401/trax   the dashboard
//
// Outside Development the dashboard is not served (/trax is a 404) until you choose
// who may use it. See "Before deploying" in README.md.
// ---------------------------------------------------------------------------

using Trax.Dashboard.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Scheduler.Trains.HelloWorld;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

var builder = WebApplication.CreateBuilder(args);

// -- 1. Trax: the effect system, the mediator and the scheduler -----------------
// AddTrax comes before AddTraxDashboard, which throws without it.
//   UseInMemory()          where runs are recorded. Swap for UsePostgres(connectionString)
//                          (package Trax.Effect.Data.Postgres) to keep them across restarts
//                          and share the work between several scheduler processes.
//   SaveTrainParameters()  stores each run's input and output, which the dashboard shows.
//   AddJunctionProgress()  records the running junction and lets the dashboard cancel a run
//                          between junctions.
//   AddMediator(...)       registers every train in the assemblies named, under its
//                          interface. Schedule<T> refuses to start for a train it did not
//                          register.
//   AddScheduler(...)      stores each Schedule(...) as a manifest at startup and runs it.
//                          The dashboard needs it: UseTraxDashboard() refuses to start
//                          without it.
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

// -- 2. Dashboard (Development only) ------------------------------------------
// The dashboard can queue, run and cancel trains and change scheduler settings.
// UseTraxDashboard() refuses to start until AddTraxDashboard says who may use it.
// AllowAnonymousDashboard() lets anyone who can reach the port use it, so it is declared
// only here, and the dashboard is not served outside Development. To serve it elsewhere,
// add authentication, choose RequirePolicy("<policy>") or RequireRoles("<role>") instead,
// and drop the IsDevelopment() checks: https://traxsharp.net/docs/dashboard.
if (builder.Environment.IsDevelopment())
    builder.AddTraxDashboard(dashboard => dashboard.AllowAnonymousDashboard());

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseTraxDashboard();

app.Run();
