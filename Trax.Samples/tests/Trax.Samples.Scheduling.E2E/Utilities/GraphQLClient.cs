using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Trax.Samples.Scheduling.E2E.Utilities;

/// <summary>Posts GraphQL documents to <c>/trax/graphql</c>, with or without an API key.</summary>
public sealed class GraphQLClient(HttpClient http, string? apiKey)
{
    public async Task<GraphQLResponse> Send(string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query }),
        };
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);

        using var response = await http.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return new GraphQLResponse(
            JsonDocument.Parse(body).RootElement.Clone(),
            response.StatusCode,
            body
        );
    }
}
