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
// 5. Shows the shape-diff guardrail refusing an edit that would change the
//    response shipped clients read.
//
// Notes:
// - The client no longer touches the database. All admin actions go through
//   the GraphQL mutations exposed by the server's persisted-operations
//   subsystem. The same mutations are what the Trax dashboard calls.
// - Run after starting Trax.Samples.PersistedOperations.Api with `dotnet run`, which
//   starts it in Development on http://localhost:5240.
// ─────────────────────────────────────────────────────────────────────────────

using System.Net.Http.Json;
using System.Text.Json;

const string ApiUrl = "http://localhost:5240/trax/graphql/";

using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };

// The upload mutation requires the Operator role. This demo key is registered by the API only in
// Development; against any other environment the upload is refused.
http.DefaultRequestHeaders.Add("X-Api-Key", "operator-key-do-not-use-in-production");

try
{
    // 1. Upload the manifest via the mutation.
    foreach (var op in LoadManifest())
    {
        await UploadAsync(http, op.Id, op.Document);
        Console.WriteLine($"Uploaded {op.Id}");
    }

    // 2. Call greet_v1 by id.
    Console.WriteLine("\n--- greet_v1 (Alice) ---");
    Console.WriteLine(
        await PostByIdAsync(http, "greet_v1", new { input = new { name = "Alice" } })
    );

    // 3. Call lookupUser_v1 by id.
    Console.WriteLine("\n--- lookupUser_v1 (user-42) ---");
    Console.WriteLine(
        await PostByIdAsync(http, "lookupUser_v1", new { input = new { userId = "user-42" } })
    );

    // 4. Hot-fix demo: rewrite greet_v1 without touching the client. Swapping
    //    the order of the two fields keeps the response shape (same fields, same
    //    types), so the shape-diff guardrail accepts it with no bypass, and the
    //    next request by the same id runs the new document: the keys come back in
    //    the new order. Each run of this client leaves greet_v1 hot-fixed, and the
    //    manifest upload in step 1 restores the original order on the next run.
    await UploadAsync(
        http,
        "greet_v1",
        "query Greet($input: GreetInput!) { discover { greeting { greet(input: $input) { greetedAt greeting } } } }",
        description: "demo hot-fix (fields reordered, same shape)"
    );
    Console.WriteLine("\nHot-fixed greet_v1 (no client redeploy needed).");

    Console.WriteLine("\n--- greet_v1 after hot-fix (Alice) ---");
    var afterHotFix = await PostByIdAsync(http, "greet_v1", new { input = new { name = "Alice" } });
    Console.WriteLine(afterHotFix);
    if (!afterHotFix.Contains("{\"greetedAt\":", StringComparison.Ordinal))
        throw new InvalidOperationException(
            "Hot-fix did not take effect: greetedAt is not the first key. The server kept serving "
                + "the previously compiled document for greet_v1."
        );
    Console.WriteLine("Hot-fix verified: the server ran the new document.");

    // 5. The guardrail: an edit that changes the response shape (here, an extra
    //    field) would break clients already shipped against greet_v1, so the
    //    server refuses it unless the operator passes bypassShapeDiff.
    var refusal = await TryUploadAsync(
        http,
        "greet_v1",
        "query Greet($input: GreetInput!) { discover { greeting { greet(input: $input) { greeting greetedAt __typename } } } }"
    );
    Console.WriteLine($"\nShape-changing edit refused: {refusal}");
    if (refusal is null)
        throw new InvalidOperationException(
            "The server accepted a shape-changing edit to greet_v1: the shape-diff guardrail did "
                + "not refuse it."
        );
}
catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
{
    // A refused upload (the API not in Development, so no demo key), an API that is not running,
    // or a step that did not do what it should: say which, without a stack trace.
    Console.Error.WriteLine($"\n{ex.Message}");
    return 1;
}

return 0;

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
    var error = await TryUploadAsync(http, id, document, description, bypassShapeDiff);
    if (error is not null)
        throw new InvalidOperationException($"uploadPersistedOperation failed for '{id}': {error}");
}

// Returns null when the upload succeeded, otherwise the first error the server reported.
static async Task<string?> TryUploadAsync(
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

    JsonDocument doc;
    try
    {
        doc = JsonDocument.Parse(raw);
    }
    catch (JsonException)
    {
        return $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {raw}";
    }

    using var _ = doc;
    var root = doc.RootElement;

    // A refusal before the mutation runs (no credential, or not the Operator role) comes back
    // as a top-level error with no data.
    if (
        !root.TryGetProperty("data", out var data)
        || data.ValueKind != JsonValueKind.Object
        || !data.TryGetProperty("operations", out var operations)
        || operations.ValueKind != JsonValueKind.Object
    )
        return root.TryGetProperty("errors", out var topLevel) && topLevel.GetArrayLength() > 0
            ? DescribeError(topLevel[0])
            : $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {raw}";

    var payload = operations
        .GetProperty("persistedOperations")
        .GetProperty("uploadPersistedOperation");
    if (payload.GetProperty("success").GetBoolean())
        return null;

    var errors = payload.GetProperty("errors");
    return errors.GetArrayLength() > 0 ? errors[0].GetRawText() : "(no error)";
}

// "message (CODE)" for a GraphQL error, or the message alone when it carries no code.
static string DescribeError(JsonElement error)
{
    var message = error.TryGetProperty("message", out var m) ? m.GetString() : error.GetRawText();
    return
        error.TryGetProperty("extensions", out var extensions)
        && extensions.TryGetProperty("code", out var code)
        ? $"{message} ({code.GetString()})"
        : message ?? error.GetRawText();
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
