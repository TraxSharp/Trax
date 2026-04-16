// ─────────────────────────────────────────────────────────────────────────────
// Trax Api Audit Sample
//
// Minimal GraphQL host demonstrating Trax.Api.GraphQL.Audit with a console sink.
// Each GraphQL request produces a TraxAuditEntry that flows through the bounded
// channel, gets batched by the writer, and lands in the log as a single line.
//
// Authentication: fake API key via X-Api-Key header (demo only, NO WARRANTY).
//   alice-key   resolves to user alice
//   bob-key     resolves to user bob
//
// Try it:
//   dotnet run --project Trax.Samples.ApiAudit
//   curl -H "X-Api-Key: alice-key" -H "Content-Type: application/json" \
//        -d '{"query":"{ dispatch { echo(input:{message:\"hi\"}) { output { echoed } } } }"}' \
//        http://localhost:5220/trax/graphql
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Audit;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.ApiAudit.Auth;
using Trax.Samples.ApiAudit.Sinks;
using Trax.Samples.ApiAudit.Trains;

var builder = WebApplication.CreateBuilder(args);

var traxConnectionString =
    builder.Configuration.GetConnectionString("TraxDatabase") ?? "Data Source=apiaudit.db";

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Authentication, fake API key for demonstration (NO WARRANTY) ──
builder.Services.AddTraxApiKeyAuth(keys =>
    keys.Add(SampleKeys.AliceKey, id: "alice", nameof(AuditRole.User))
        .Add(SampleKeys.BobKey, id: "bob", nameof(AuditRole.User))
);
builder.Services.AddAuthorization();

// ── Trax core ──
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects.UseSqlite(traxConnectionString).AddJson().SaveTrainParameters()
        )
        .AddMediator(typeof(IEchoTrain).Assembly)
);

// ── GraphQL + Audit (NO WARRANTY) ──
// RequireAuthorization() gates HTTP execution behind the API key. The Banana
// Cake Pop tool page (GET /trax/graphql) and schema introspection are
// governed independently and stay reachable, so a developer can load the
// IDE without credentials and only needs a key to run an actual operation.
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .RequireAuthorization()
        .AddAudit<ConsoleAuditSink>(opts =>
        {
            opts.BatchSize = 10;
            opts.FlushInterval = TimeSpan.FromMilliseconds(250);
        })
);

builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");
app.Run();

namespace Trax.Samples.ApiAudit
{
    public partial class Program;
}
