// ─────────────────────────────────────────────────────────────────────────────
// Trax Persisted Operations sample — Client
//
// 1. Connects to the same Trax Postgres as the API process.
// 2. Uploads the manifest of (id, document) pairs through IPersistedOperationStore.
// 3. Sends GraphQL requests by id only — the server resolves to the stored
//    document and dispatches the call to the underlying train.
// 4. Demonstrates the hot-fix flow by re-uploading a shape-preserving
//    edit; the next request runs the new document without redeploying the
//    client.
//
// Run after starting Trax.Samples.PersistedOperations.Api.
// ─────────────────────────────────────────────────────────────────────────────

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;

const string ConnectionString =
    "Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123";
const string ApiUrl = "http://localhost:5000/trax/graphql/";

await using var services = new ServiceCollection()
    .AddLogging(b => b.AddSimpleConsole())
    .AddTrax(trax => trax.AddEffects(effects => effects.UsePostgres(ConnectionString)))
    .AddPersistedOperationStore(ConnectionString)
    .BuildServiceProvider();

var store = services.GetRequiredService<IPersistedOperationStore>();

// 1. Upload the manifest.
foreach (var op in LoadManifest())
{
    await store.UpsertAsync(op.Id, op.Document, options: null, CancellationToken.None);
    Console.WriteLine($"Uploaded {op.Id}");
}

// 2. Call greet_v1 by id.
using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
Console.WriteLine("\n--- greet_v1 (Alice) ---");
Console.WriteLine(await PostByIdAsync(http, "greet_v1", new { input = new { name = "Alice" } }));

// 3. Call lookupUser_v1 by id.
Console.WriteLine("\n--- lookupUser_v1 (user-42) ---");
Console.WriteLine(
    await PostByIdAsync(http, "lookupUser_v1", new { input = new { userId = "user-42" } })
);

// 4. Hot-fix demo: rewrite greet_v1 with a shape-preserving change. In a
//    real fix you might swap to a different train or change a filter; here
//    we change a static argument to demonstrate that the server-side change
//    flows through without touching the client. The shape-diff guardrail
//    permits this because the response shape is unchanged.
await store.UpsertAsync(
    "greet_v1",
    "query Greet($input: GreetInput!) { discover { greeting { greet(input: $input) { greeting greetedAt } } } }",
    options: new UpsertOptions { Description = "demo hot-fix (shape-preserving rewrite)" },
    CancellationToken.None
);
Console.WriteLine("\nHot-fixed greet_v1 (no client redeploy needed).");

Console.WriteLine("\n--- greet_v1 after hot-fix (Alice) ---");
Console.WriteLine(await PostByIdAsync(http, "greet_v1", new { input = new { name = "Alice" } }));

static async Task<string> PostByIdAsync(HttpClient http, string id, object variables)
{
    var body = new { id, variables };
    var resp = await http.PostAsJsonAsync(string.Empty, body);
    return await resp.Content.ReadAsStringAsync();
}

static IEnumerable<ManifestEntry> LoadManifest()
{
    var path = Path.Combine(AppContext.BaseDirectory, "manifest.json");
    using var stream = File.OpenRead(path);
    var doc = JsonDocument.Parse(stream);
    foreach (var entry in doc.RootElement.GetProperty("operations").EnumerateArray())
    {
        yield return new ManifestEntry(
            entry.GetProperty("id").GetString()!,
            entry.GetProperty("document").GetString()!
        );
    }
}

internal sealed record ManifestEntry(string Id, string Document);
