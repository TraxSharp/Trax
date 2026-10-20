using System.Net.Http.Json;
using System.Text.Json;

namespace Shopfront.Tests.IntegrationTests;

internal static class GraphQL
{
    public static async Task<JsonElement> Post(
        this HttpClient client,
        string query,
        object? variables = null
    )
    {
        var response = await client.PostAsJsonAsync("/trax/graphql", new { query, variables });
        return (await response.Content.ReadFromJsonAsync<JsonElement>())!;
    }

    // The extensions.code of each error, empty when there are none.
    public static IEnumerable<string?> ErrorCodes(this JsonElement response) =>
        response.TryGetProperty("errors", out var errors)
            ? errors
                .EnumerateArray()
                .Select(e =>
                    e.TryGetProperty("extensions", out var x) && x.TryGetProperty("code", out var c)
                        ? c.GetString()
                        : null
                )
            : [];
}
