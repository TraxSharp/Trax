using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Trax.Samples.Auth.E2E.Utilities;

public class GraphQLClient(HttpClient httpClient)
{
    /// <summary>
    /// Posts <paramref name="query"/> as <paramref name="caller"/>. Pass
    /// <paramref name="operationName"/> to send the request's <c>operationName</c> field, which is
    /// what the audit trail records as the operation name; naming the operation inside the
    /// document alone is not enough.
    /// </summary>
    public async Task<GraphQLResponse> SendAsync(
        string query,
        Caller caller,
        string? operationName = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query, operationName }),
        };
        caller.Apply(request);

        using var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException(
                $"Empty response body from the GraphQL endpoint (HTTP {(int)response.StatusCode})."
            );

        return new GraphQLResponse(
            JsonSerializer.Deserialize<JsonElement>(body),
            response.StatusCode
        );
    }
}
