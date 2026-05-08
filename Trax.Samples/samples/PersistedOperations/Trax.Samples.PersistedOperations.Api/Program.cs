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
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.PersistedOperations;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? "Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123";

builder.Services.AddAuthorization();

// Trax: Postgres effects + mediator (which discovers trains in the library
// assembly). No scheduler — every train in this sample is a query handled
// directly on the API server via GraphQL.
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(GraphQLNamespaces).Assembly)
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

var app = builder.Build();

// Persisted-op enforcement runs before the GraphQL endpoint. In a real
// deployment, ASP.NET authentication middleware sits in front of this and
// the persisted-op lookup happens AFTER Trax's auth interceptor inside the
// HC pipeline.
app.UsePersistedOperationsEnforcement();
app.UseRouting();
app.UseTraxGraphQL();

app.Run();

/// <summary>
/// Visible to <c>WebApplicationFactory&lt;Program&gt;</c> for E2E tests.
/// </summary>
public partial class Program;
