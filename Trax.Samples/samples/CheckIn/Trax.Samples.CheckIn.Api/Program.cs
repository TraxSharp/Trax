// ─────────────────────────────────────────────────────────────────────────────
// Trax CheckIn: machines written only as features
//
// The three machines (a passenger's booking, a gate agent's desk, the flight) have no C# class and no hand-written
// rule. A team writes features/*.feature and features/glossary.json; `trax machine from-gherkin` derives machines/
// from them (one IR per machine, host.json with who may do what, access.txt to review); this host serves that
// directory as committed with ServeFeatures. The one piece of C# behind the machines is the boarding pass train the
// booking's Issuing state invokes.
//
// Prerequisites (from the Trax.Samples folder):
//   docker compose up -d database
//   dotnet run --project samples/CheckIn/Trax.Samples.CheckIn.Api
//   cd samples/CheckIn/Trax.Samples.CheckIn.Web && npm ci && npm run dev
// Then open http://localhost:5281 once per person, and sign in with a name and a role. samples/CheckIn/demo.sh runs it
// all. After editing a feature: trax machine from-gherkin --features samples/CheckIn/features --out samples/CheckIn/machines
//
// Auth: a token per person from POST /dev/token { "name": "Ada", "role": "passenger" }, Development only, signed with
// a demo key that refuses to start anywhere else. Outside Development the host has no sign-in, so nothing is served.
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Dashboard.Extensions;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Effect.Configuration.ClientExport;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.CheckIn.Api;
using Trax.Samples.CheckIn.Api.Auth;
using Trax.Samples.CheckIn.Trains;
using Trax.Samples.Shared.Auth;
using Trax.Scheduler.Extensions;
using Trax.StateMachine.Persistence;

var builder = WebApplication.CreateBuilder(args);

// An export of the client manifest (--trax-export-client) opens no database.
var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? (
        TraxClientExport.Requested
            ? "Host=localhost"
            : throw new InvalidOperationException("Set ConnectionStrings:TraxDatabase.")
    );

// ── Trax ────────────────────────────────────────────────────────────────────
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddJson()
                // Reads a run's cancel flag between junctions, so a booking leaving Issuing stops its run.
                .AddJunctionProgress()
        )
        // The machines from-gherkin wrote, with their access, the policies between them (a gate agent opening their
        // desk opens check-in for every booking, closing the gate there makes every booking still open miss the
        // flight), the bookings and departures views, and the flight itself, the system instance the host starts. The
        // boarding pass train is found in this assembly by its interface's full name.
        .AddStateMachines(o =>
        {
            o.ServeFeatures("machines", typeof(IssueBoardingPassTrain).Assembly);
            // How often the host sweeps for due timers and ended runs it was not told of. Development and the feature
            // tests shorten it, since the desk's and the flight's timers are seconds long.
            if (
                builder.Configuration.GetValue<TimeSpan?>("CheckIn:MachineSweepInterval") is
                { } sweep
            )
            {
                o.InvokeOutcomeSweepInterval = sweep;
                o.InvokeOutcomeSweepIntervalWhileListening = sweep;
            }
        })
        .AddMediator(typeof(IssueBoardingPassTrain).Assembly)
        // Runs the boarding pass train a booking's Issuing state invokes, on this host's local workers.
        .AddScheduler(scheduler =>
            scheduler.ConfigureLocalWorkers(workers =>
                workers.PollingInterval = TimeSpan.FromMilliseconds(250)
            )
        )
);

// ── Sign-in: one token per person, Development only ─────────────────────────
builder.AddDemoSignIn(DemoTokens.Issuer);
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();

// ── GraphQL ─────────────────────────────────────────────────────────────────
// Persisted documents only, everywhere: Trax generates and registers one per machine operation and per view, the page
// holds their ids in its generated client, and the feature tests send the same ids from the client manifest.
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .ExposeOperationQueries()
        .GateOperations(roles: CheckInRoles.Operator)
        .UsePersistedOperations(persisted =>
            persisted.SingleNode().RequirePersisted(true).DisableIntrospection()
        )
);
builder.Services.AddHealthChecks().AddTraxHealthCheck();

// The page's origin: the Vite dev server unless CheckIn:PageOrigin names another (the browser tests serve the built
// page from one of their own).
var pageOrigin = builder.Configuration["CheckIn:PageOrigin"] ?? "http://localhost:5281";
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(pageOrigin).AllowAnyHeader().AllowAnyMethod()
    )
);

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

// A token for one person: a name and a role, nothing else. Development only, like the key that signs it.
app.MapDemoSignIn(DemoTokens.Issuer);

if (app.Environment.IsDevelopment())
{
    // The operations console, its Board tab included. It asks for an operator's token on every load.
    app.UseTraxApiDashboard(dashboard => dashboard.AllowAnonymousShell());
}

await app.RunTraxAsync(args);

namespace Trax.Samples.CheckIn.Api
{
    public partial class Program;
}
