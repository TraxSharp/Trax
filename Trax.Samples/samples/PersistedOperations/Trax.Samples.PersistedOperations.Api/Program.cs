// ─────────────────────────────────────────────────────────────────────────────
// Trax Persisted Operations sample — GraphQL API
//
// This sample demonstrates Trax.Api.GraphQL.PersistedOperations end-to-end:
//
//   - Real trains (GreetTrain, LookupUserTrain) registered through AddMediator
//     and exposed via [TraxQuery] on the GraphQL schema.
//   - The API only accepts persisted operations: clients send `id` and the
//     server resolves to the stored document via IOperationDocumentStorage.
//   - Operators can hot-fix a persisted document (or the underlying junction
//     code) without redeploying clients, as long as the response shape stays
//     compatible. The shape-diff guardrail in IPersistedOperationStore
//     enforces this contract on every edit.
//   - The management mutations (upload, deactivate, ...) live under the
//     `operations` namespace and require the Operator role. The demo
//     operator key (X-Api-Key: operator-key-do-not-use-in-production), the
//     dev_ allowlist and the dashboard exist only in Development, which
//     `dotnet run` starts through Properties/launchSettings.json.
//
// Run alongside the Client project to see the upload + query + hot-fix loop.
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.EntityFrameworkCore;
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.PersistedOperations;
using Trax.Samples.PersistedOperations.Api.Auth;
using Trax.Samples.PersistedOperations.Models;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? "Host=localhost;Port=5432;Database=trax_persisted_operations;Username=trax;Password=trax123";

var isDevelopment = builder.Environment.IsDevelopment();

// The demo operator key is published in this repository, so it is registered
// only in Development. Anywhere else no credential exists until you register a
// real scheme (API key from a secret store, JWT, cookies, ...), so the
// management mutations and the gated userNotes query are refused.
// AddAuthentication() registers IAuthenticationSchemeProvider either way, so
// UseAuthentication() and Trax's QueryModelAuthenticationInterceptor resolve.
if (isDevelopment)
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoKeys.OperatorKey, id: "operator", DemoKeys.OperatorRole)
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// EF DbContext that backs the gated UserNote query model. Registering it as
// a factory matches how AddTraxGraphQL().AddDbContext<>() resolves the
// context per query without sharing across requests.
builder.Services.AddDbContextFactory<UserNotesDbContext>(o => o.UseNpgsql(connectionString));

// Trax: Postgres effects + mediator + scheduler. The persisted-operations
// sample is GraphQL-only (no queued work), but the dashboard's existing
// pages (manifest groups, work queue, dead letters) depend on the scheduler
// services even when no jobs are dispatched.
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(GraphQLNamespaces).Assembly)
        .AddScheduler(scheduler => scheduler)
);

// GraphQL schema with persisted-operations enforcement enabled. AddTraxGraphQL
// auto-discovers [TraxQuery]-attributed trains from the registered mediator
// assemblies and exposes them under their declared namespace. The UserNote
// query model is gated by [TraxAuthorize], which flips on HotChocolate's
// @authorize directive in the schema. The persisted-operation validator has
// to seed an IAuthorizationHandler into its validator state to coexist with
// that directive, otherwise UpsertAsync throws MissingStateException.
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .AddDbContext<UserNotesDbContext>()
        .UsePersistedOperations(opts =>
        {
            opts.RequirePersisted(true)
                .LogNonPersistedRequests(true)
                // One process serves this endpoint and writes the store, so an upload reaches
                // every cached copy without a broadcast. A second node needs
                // UseRabbitMqInvalidation(...) instead; the host refuses to start with neither.
                .SingleNode();

            // Dev-prefixed operations bypass enforcement so developers can iterate on a query
            // without round-tripping through the manifest uploader. The operation name is chosen
            // by the caller, so outside Development this would let anyone run any document by
            // naming it dev_something: it is registered only here.
            if (isDevelopment)
                opts.AllowOperationsMatching(id => id.StartsWith("dev_"));
        })
        // UsePersistedOperations exposes the management mutations under `operations`. Gate
        // that namespace, leaving the persisted trains on the rest of the endpoint reachable.
        .GateOperations(roles: DemoKeys.OperatorRole)
);

// Dashboard: mounts the operations control room (including the Persisted
// Operations management page) under /trax. The page shows up because
// UsePersistedOperations above registers IPersistedOperationsService. The
// dashboard refuses to start without a posture; this sample serves it only in
// Development and opens it there. Anywhere else, register it with
// RequirePolicy(...) or RequireRoles(...) instead.
if (isDevelopment)
    builder.AddTraxDashboard(dashboard => dashboard.AllowAnonymousDashboard());

var app = builder.Build();

// Create the notes.user_notes table on first run so the gated query model
// has something to query. Plain idempotent SQL keeps the bootstrap testable
// and side-steps EF's GenerateCreateScript, which would throw 42P07 on a
// re-run.
using (var scope = app.Services.CreateScope())
{
    var db = scope
        .ServiceProvider.GetRequiredService<IDbContextFactory<UserNotesDbContext>>()
        .CreateDbContext();
    db.Database.ExecuteSqlRaw(
        """
        CREATE SCHEMA IF NOT EXISTS notes;
        CREATE TABLE IF NOT EXISTS notes.user_notes (
            id          bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            title       text NOT NULL DEFAULT '',
            body        text NOT NULL DEFAULT '',
            created_at  timestamptz NOT NULL DEFAULT now()
        );
        """
    );
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Enforcement runs inside HotChocolate's pipeline (registered by
// UsePersistedOperations), so it covers HTTP and WebSocket alike and needs no
// middleware here.
app.UseTraxGraphQL();
if (isDevelopment)
    app.UseTraxDashboard();

app.Run();

/// <summary>
/// Visible to <c>WebApplicationFactory&lt;Program&gt;</c> for E2E tests.
/// </summary>
public partial class Program;
