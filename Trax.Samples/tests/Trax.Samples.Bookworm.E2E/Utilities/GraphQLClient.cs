using System.Net.Http.Json;
using System.Text.Json;

namespace Trax.Samples.Bookworm.E2E.Utilities;

/// <summary>Minimal GraphQL-over-HTTP helper for the Bookworm E2E suite.</summary>
public sealed class GraphQLClient(HttpClient httpClient)
{
    /// <summary>
    /// POSTs a query (optionally with an API key) and returns the parsed JSON response document.
    /// </summary>
    public async Task<JsonDocument> PostAsync(string query, string? apiKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query }),
        };
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);

        using var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }

    /// <summary>True if the response carries a non-empty <c>errors</c> array.</summary>
    public static bool HasErrors(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("errors", out var errors)
        && errors.ValueKind == JsonValueKind.Array
        && errors.GetArrayLength() > 0;
}
