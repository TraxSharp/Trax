// ─────────────────────────────────────────────────────────────────────────────
// Trax Persisted Operations sample — Client
//
// 1. Reads the manifest of (id, document) pairs.
// 2. Uploads each through the uploadPersistedOperation GraphQL mutation.
// 3. Sends GraphQL requests by id only — the server resolves to the stored
//    document and dispatches the call to the underlying train.
// 4. Demonstrates the hot-fix flow by re-uploading a shape-preserving
//    edit; the next request runs the new document without redeploying the
//    client.
//
// Notes:
// - The client no longer touches the database. All admin actions go through
//   the GraphQL mutations exposed by the server's persisted-operations
//   subsystem. The same mutations are what the Trax dashboard calls.
// - Run after starting Trax.Samples.PersistedOperations.Api.
// ─────────────────────────────────────────────────────────────────────────────

using System.Net.Http.Json;
using System.Text.Json;

const string ApiUrl = "http://localhost:5000/trax/graphql/";

using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };

// 1. Upload the manifest via the mutation.
foreach (var op in LoadManifest())
{
    await UploadAsync(http, op.Id, op.Document);
    Console.WriteLine($"Uploaded {op.Id}");
}

// 2. Call greet_v1 by id.
Console.WriteLine("\n--- greet_v1 (Alice) ---");
Console.WriteLine(await PostByIdAsync(http, "greet_v1", new { input = new { name = "Alice" } }));

// 3. Call lookupUser_v1 by id.
Console.WriteLine("\n--- lookupUser_v1 (user-42) ---");
Console.WriteLine(
    await PostByIdAsync(http, "lookupUser_v1", new { input = new { userId = "user-42" } })
);

// 4. Hot-fix demo: rewrite greet_v1 with a GENUINELY different document
//    that produces a visibly different response. The new document adds an
//    extra `__typename` field inside the greet selection, which changes
//    the response shape, so BypassShapeDiff is required. Before the
//    invalidation fix in Trax.Api.GraphQL.PersistedOperations, the
//    HotChocolate request pipeline kept serving the previous compiled
//    operation until the process restarted; with the fix in place the
//    next request runs the new document.
await UploadAsync(
    http,
    "greet_v1",
    "query Greet($input: GreetInput!) { discover { greeting { greet(input: $input) { greeting greetedAt __typename } } } }",
    description: "demo hot-fix (adds __typename)",
    bypassShapeDiff: true
);
Console.WriteLine("\nHot-fixed greet_v1 (no client redeploy needed).");

Console.WriteLine("\n--- greet_v1 after hot-fix (Alice) ---");
var afterHotFix = await PostByIdAsync(http, "greet_v1", new { input = new { name = "Alice" } });
Console.WriteLine(afterHotFix);
if (!afterHotFix.Contains("__typename"))
    throw new InvalidOperationException(
        "Hot-fix did not take effect: response is missing __typename. "
            + "This indicates HotChocolate's IDocumentCache / IPreparedOperationCache "
            + "were not invalidated when the persisted operation was upserted."
    );
Console.WriteLine("Hot-fix verified: __typename present in response.");

static async Task<string> PostByIdAsync(HttpClient http, string id, object variables)
{
    var body = new { id, variables };
    var resp = await http.PostAsJsonAsync(string.Empty, body);
    return await resp.Content.ReadAsStringAsync();
}

static async Task UploadAsync(
    HttpClient http,
    string id,
    string document,
    string? description = null,
    bool bypassShapeDiff = false
)
{
    const string mutation = """
        mutation Upload($input: UploadPersistedOperationInput!) {
          operations {
            persistedOperations {
              uploadPersistedOperation(input: $input) {
                success
                errors { code message }
              }
            }
          }
        }
        """;
    var input = new Dictionary<string, object?>
    {
        ["id"] = id,
        ["document"] = document,
        ["bypassShapeDiff"] = bypassShapeDiff,
    };
    if (description is not null)
        input["description"] = description;

    var body = new { query = mutation, variables = new { input } };
    var resp = await http.PostAsJsonAsync(string.Empty, body);
    var raw = await resp.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(raw);
    var payload = doc
        .RootElement.GetProperty("data")
        .GetProperty("operations")
        .GetProperty("persistedOperations")
        .GetProperty("uploadPersistedOperation");
    if (!payload.GetProperty("success").GetBoolean())
    {
        var errors = payload.GetProperty("errors");
        var first = errors.GetArrayLength() > 0 ? errors[0].GetRawText() : "(no error)";
        throw new InvalidOperationException($"uploadPersistedOperation failed for '{id}': {first}");
    }
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
