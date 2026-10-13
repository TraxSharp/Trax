// ─────────────────────────────────────────────────────────────────────────────
// Trax Persisted Operations sample — Client
//
// Three stories, in order:
//
//   1. A shipped app calls the API by id. It sends greet_v1 and a name, never
//      a query, and an inline query is refused.
//   2. Fix it without shipping a new app. The operator re-uploads greet_v1
//      with style FORMAL; the same call now answers "Good day, Alice."
//   3. A fix that would break shipped apps is refused. An upload that renames
//      the field the app reads fails the shape-diff guardrail, and greet_v1
//      keeps its answer.
//
// Run after starting Trax.Samples.PersistedOperations.Api with `dotnet run`,
// which starts it in Development on http://localhost:5240.
// ─────────────────────────────────────────────────────────────────────────────

using System.Net.Http.Json;
using System.Text.Json;

const string ApiUrl = "http://localhost:5240/trax/graphql/";

const string FormalFix =
    "query Greet($name: String!) { discover { greeting { greet(input: { name: $name, style: FORMAL }) { greeting } } } }";

const string RenamedField =
    "query Greet($name: String!) { discover { greeting { greet(input: { name: $name, style: FORMAL }) { message: greeting } } } }";

// The app sends no credential. The operator's uploads carry the demo key, which the API registers
// only in Development; against any other environment the uploads are refused.
using var app = new HttpClient { BaseAddress = new Uri(ApiUrl) };
using var operatorHttp = new HttpClient { BaseAddress = new Uri(ApiUrl) };
operatorHttp.DefaultRequestHeaders.Add("X-Api-Key", "operator-key-do-not-use-in-production");

try
{
    // Upload the manifest. On a re-run this puts greet_v1 back to CASUAL: the response shape is
    // the same, so the guardrail accepts it like any other fix.
    foreach (var op in LoadManifest())
    {
        await UploadAsync(operatorHttp, op.Id, op.Document);
        Console.WriteLine($"Uploaded {op.Id}");
    }

    // 1. The app calls by id.
    Console.WriteLine("\n1. The app calls greet_v1 for Alice");
    Expect(await GreetAsync(app), "Hey Alice!");

    var inline = await app.PostAsJsonAsync(
        string.Empty,
        new
        {
            query = "query { discover { greeting { greet(input: { name: \"Eve\", style: FORMAL }) { greeting } } } }",
        }
    );
    var inlineBody = await inline.Content.ReadAsStringAsync();
    if (!inlineBody.Contains("PERSISTED_OPERATION_REQUIRED", StringComparison.Ordinal))
        throw new InvalidOperationException($"The server ran an inline query: {inlineBody}");
    Console.WriteLine($"   An inline query is refused: {inlineBody}");

    // 2. The operator fixes greet_v1; the app does not change.
    await UploadAsync(operatorHttp, "greet_v1", FormalFix);
    Console.WriteLine("\n2. The operator re-uploads greet_v1 with style FORMAL");
    Expect(await GreetAsync(app), "Good day, Alice.");

    // 3. An upload that renames the field the app reads is refused.
    var refusal = await TryUploadAsync(operatorHttp, "greet_v1", RenamedField);
    if (refusal is null)
        throw new InvalidOperationException(
            "The server accepted an upload that renames the field the app reads."
        );
    Console.WriteLine($"\n3. Renaming greeting to message is refused: {refusal}");
    Expect(await GreetAsync(app), "Good day, Alice.");
}
catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
{
    // A refused upload (the API not in Development, so no demo key), an API that is not running,
    // or a step that did not do what it should: say which, without a stack trace.
    Console.Error.WriteLine($"\n{ex.Message}");
    return 1;
}

return 0;

// The call the shipped app makes: an id and its variables, nothing else.
static async Task<string> GreetAsync(HttpClient app)
{
    var resp = await app.PostAsJsonAsync(
        string.Empty,
        new { id = "greet_v1", variables = new { name = "Alice" } }
    );
    var raw = await resp.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(raw);
    return
        doc.RootElement.TryGetProperty("data", out var data)
        && data.ValueKind == JsonValueKind.Object
        ? data.GetProperty("discover")
            .GetProperty("greeting")
            .GetProperty("greet")
            .GetProperty("greeting")
            .GetString()
            ?? raw
        : raw;
}

static void Expect(string actual, string expected)
{
    Console.WriteLine($"   greet_v1 answers: {actual}");
    if (actual != expected)
        throw new InvalidOperationException($"Expected \"{expected}\", got {actual}");
}

static async Task UploadAsync(HttpClient http, string id, string document)
{
    var error = await TryUploadAsync(http, id, document);
    if (error is not null)
        throw new InvalidOperationException($"uploadPersistedOperation failed for '{id}': {error}");
}

// Returns null when the upload succeeded, otherwise the first error the server reported.
static async Task<string?> TryUploadAsync(HttpClient http, string id, string document)
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
    var body = new { query = mutation, variables = new { input = new { id, document } } };
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
