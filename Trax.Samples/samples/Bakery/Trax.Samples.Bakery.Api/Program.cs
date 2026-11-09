// ─────────────────────────────────────────────────────────────────────────────
// Trax Bakery: machines written only as features
//
// The bakery's three machines (a customer's order, a baker's shift, the shop) have no C# class and no hand-written
// rule. A team writes features/*.feature and features/glossary.json; `trax machine from-gherkin` derives machines/
// from them (one IR per machine, host.json with who may do what, access.txt to review); this host serves that
// directory as committed with ServeFeatures. The one piece of C# behind the machines is the payment train the order's
// Paying state invokes.
//
// Prerequisites (from the Trax.Samples folder):
//   docker compose up -d database
//   dotnet run --project samples/Bakery/Trax.Samples.Bakery.Api
//   cd samples/Bakery/Trax.Samples.Bakery.Web && npm ci && npm run dev
// Then open http://localhost:5281 once per person, and sign in with a name and a role. samples/Bakery/demo.sh runs it
// all. After editing a feature: trax machine from-gherkin --features samples/Bakery/features --out samples/Bakery/machines
//
// Auth: a token per person from POST /dev/token { "name": "Ada", "role": "customer" }, Development only, signed with a
// demo key that refuses to start anywhere else. Outside Development the host has no sign-in, so nothing is served.
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
using Trax.Samples.Bakery.Api;
using Trax.Samples.Bakery.Api.Auth;
using Trax.Samples.Bakery.Trains;
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
                // Reads a run's cancel flag between junctions, so an order leaving Paying stops its payment run.
                .AddJunctionProgress()
        )
        // The machines from-gherkin wrote, with their access, the policies between them (a baker clocking in or out
        // opens or closes the shop; firing the oven bakes every waiting order), the shop sign and order views, and the
        // shop itself, the system instance the host starts. The payment train is found in this assembly by its
        // interface's full name.
        .AddStateMachines(o =>
        {
            o.ServeFeatures("machines", typeof(TakePaymentTrain).Assembly);
            // How often the host sweeps for due timers and ended runs it was not told of. Development and the feature
            // tests shorten it, since the order's, the shift's and the shop's timers are seconds long.
            if (
                builder.Configuration.GetValue<TimeSpan?>("Bakery:MachineSweepInterval") is
                { } sweep
            )
            {
                o.InvokeOutcomeSweepInterval = sweep;
                o.InvokeOutcomeSweepIntervalWhileListening = sweep;
            }
        })
        .AddMediator(typeof(TakePaymentTrain).Assembly)
        // Runs the payment an order's Paying state invokes, on this host's local workers.
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
        .GateOperations(roles: BakeryRoles.Operator)
        .UsePersistedOperations(persisted =>
            persisted.SingleNode().RequirePersisted(true).DisableIntrospection()
        )
);
builder.Services.AddHealthChecks().AddTraxHealthCheck();

// The page's origin: the Vite dev server unless Bakery:PageOrigin names another (the browser tests serve the built page
// from one of their own).
var pageOrigin = builder.Configuration["Bakery:PageOrigin"] ?? "http://localhost:5281";
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

namespace Trax.Samples.Bakery.Api
{
    public partial class Program;
}
