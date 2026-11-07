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
// Then open http://localhost:5174 once per person, and sign in with a name and a role. samples/Bakery/demo.sh runs it
// all. After editing a feature: trax machine from-gherkin --features samples/Bakery/features --out samples/Bakery/machines
//
// Auth: a token per person from POST /dev/token { "name": "Ada", "role": "customer" }, Development only, signed with a
// demo key that refuses to start anywhere else. Outside Development the host has no sign-in, so nothing is served.
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Auth.Jwt;
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
        // The machines from-gherkin wrote, with their access, the policies between them (a baker clocking in opens the
        // shop; the shop opening bakes the waiting orders), the shop sign and order views, and the shop itself, the
        // system instance the host starts. The payment train is found in this assembly by its interface's full name.
        .AddStateMachines(o =>
        {
            o.ServeFeatures("machines", typeof(TakePaymentTrain).Assembly);
            // How often the host sweeps for due timers and ended runs it was not told of. The default suits the demo;
            // the feature tests shorten it, since the shop's five-second closing time is waited for for real.
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
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxJwtAuth(jwt =>
        jwt.UseSymmetricKey(DemoTokens.Issuer, DemoTokens.Audience, DemoTokens.SigningKey)
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();

// ── GraphQL ─────────────────────────────────────────────────────────────────
// Persisted documents only, as in production: Trax generates and registers one per machine operation and per view,
// and the page holds their ids in its generated client. Bakery:RequirePersistedOperations turns the refusal off, for a
// test that sends hand-written documents, in Development only.
var requirePersisted =
    !builder.Environment.IsDevelopment()
    || builder.Configuration.GetValue("Bakery:RequirePersistedOperations", true);
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .ExposeOperationQueries()
        .GateOperations(roles: BakeryRoles.Operator)
        .UsePersistedOperations(persisted =>
        {
            persisted
                .SingleNode()
                .RequirePersisted(requirePersisted)
                .LogNonPersistedRequests(!requirePersisted);
            if (requirePersisted)
                persisted.DisableIntrospection();
        })
);
builder.Services.AddHealthChecks().AddTraxHealthCheck();

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5174").AllowAnyHeader().AllowAnyMethod()
    )
);

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

if (app.Environment.IsDevelopment())
{
    // A token for one person: a name and a role, nothing else. Development only, like the key that signs it.
    app.MapPost(
            "/dev/token",
            (SignIn request) =>
            {
                try
                {
                    return Results.Ok(new { token = DemoTokens.Mint(request.Name, request.Role) });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            }
        )
        .AllowAnonymous();
    // The operations console, its Board tab included. It asks for an operator's token on every load.
    app.UseTraxApiDashboard(dashboard => dashboard.AllowAnonymousShell());
}

await app.RunTraxAsync(args);

namespace Trax.Samples.Bakery.Api
{
    /// <summary>What the page sends to sign one person in.</summary>
    public sealed record SignIn(string Name, string Role);

    public partial class Program;
}
