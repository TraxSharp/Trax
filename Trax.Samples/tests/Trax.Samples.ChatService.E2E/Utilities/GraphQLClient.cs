using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Samples.ChatService.E2E.Utilities;

public class GraphQLClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        RespectNullableAnnotations = false,
        RespectRequiredConstructorParameters = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<GraphQLResponse> SendAsync(
        string query,
        string? apiKey = null,
        object? variables = null
    )
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query, variables }, options: JsonOptions),
        };

        if (apiKey != null)
            request.Headers.Add("X-Api-Key", apiKey);

        var response = await httpClient.SendAsync(request).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
        {
            response.EnsureSuccessStatusCode();
            throw new InvalidOperationException("Empty response body from GraphQL endpoint.");
        }

        var json = JsonSerializer.Deserialize<JsonElement>(body, JsonOptions);
        return new GraphQLResponse(json, response.StatusCode);
    }
}
