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
//
// Run alongside the Client project to see the upload + query + hot-fix loop.
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.PersistedOperations;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? "Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123";

builder.Services.AddAuthorization();

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
// assemblies and exposes them under their declared namespace.
builder.Services.AddTraxGraphQL(graphql =>
    graphql.UsePersistedOperations(opts =>
        opts.UseDatabase(connectionString)
            .RequirePersisted(true)
            .LogNonPersistedRequests(true)
            // Dev-prefixed operations bypass enforcement so developers can
            // iterate on a query during development without round-tripping
            // through the manifest uploader.
            .AllowOperationsMatching(id => id.StartsWith("dev_"))
    )
);

// Dashboard: mounts the operations control room (including the Persisted
// Operations management page) under /trax. The page only shows up because
// IPersistedOperationsCapability is in DI thanks to UsePersistedOperations
// above.
builder.AddTraxDashboard();

var app = builder.Build();

app.UseRouting();

// Persisted-op enforcement only applies to the GraphQL endpoint. Scoping
// with UseWhen keeps it off Blazor's SignalR circuit (/_blazor/*) and the
// dashboard's static asset endpoints, so dashboard interactivity is not
// affected by the middleware's body buffering.
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/trax/graphql"),
    branch => branch.UsePersistedOperationsEnforcement()
);
app.UseTraxGraphQL();
app.UseTraxDashboard();

app.Run();

/// <summary>
/// Visible to <c>WebApplicationFactory&lt;Program&gt;</c> for E2E tests.
/// </summary>
public partial class Program;
