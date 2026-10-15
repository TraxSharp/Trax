// ─────────────────────────────────────────────────────────────────────────────
// Trax Persisted Operations sample — GraphQL API
//
// The API runs only stored documents. An app sends an id (greet_v1) and its
// variables; the server runs the document stored under that id. An operator
// can re-upload the document to change the answer without shipping a new
// app, and the shape-diff guardrail refuses an upload that would change the
// response the app reads.
//
// The upload mutations require the Operator role. The demo operator key
// (X-Api-Key: operator-key-do-not-use-in-production) and the dashboard exist
// only in Development, which `dotnet run` starts, and so does the page at
// http://localhost:5240 that plays the three stories against this API.
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.PersistedOperations;
using Trax.Samples.PersistedOperations.Api.Auth;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? "Host=localhost;Port=5432;Database=trax_persisted_operations;Username=trax;Password=trax123";

var isDevelopment = builder.Environment.IsDevelopment();

// The demo operator key is published in this repository, so it is registered only in
// Development. Anywhere else no credential exists until you register a real scheme, so every
// upload is refused.
if (isDevelopment)
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoKeys.OperatorKey, id: "operator", DemoKeys.OperatorRole)
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// The stored documents live in Postgres. The dashboard's pages need the scheduler services even
// though this sample queues no work.
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(GraphQLNamespaces).Assembly)
        .AddScheduler(scheduler => scheduler)
);

builder.Services.AddTraxGraphQL(graphql =>
    graphql.UsePersistedOperations(opts =>
        opts.RequirePersisted(true)
            .LogNonPersistedRequests(true)
            // One process serves this endpoint and writes the store, so an upload reaches
            // every cached copy without a broadcast. A second node needs
            // UseRabbitMqInvalidation(...) instead; the host refuses to start with neither.
            .SingleNode()
            // The upload mutations live under `persistedOperations`, behind their own gate.
            // Gate them to the operator; greet stays reachable to the app.
            .GateManagement(roles: DemoKeys.OperatorRole)
    )
);

// The dashboard shows the stored documents under Data > Persisted Operations. It refuses to start
// without a posture: this sample serves it only in Development, open. Anywhere else, register it
// with RequirePolicy(...) or RequireRoles(...).
if (isDevelopment)
    builder.AddTraxDashboard(dashboard => dashboard.AllowAnonymousDashboard());

var app = builder.Build();

// The page at / (wwwroot) plays the three stories against this API. Its operator uploads send the
// demo key, so it is served only in Development, with the key.
if (isDevelopment)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.UseTraxGraphQL();
if (isDevelopment)
    app.UseTraxDashboard();

app.Run();

/// <summary>
/// Visible to <c>WebApplicationFactory&lt;Program&gt;</c> for E2E tests.
/// </summary>
public partial class Program;
