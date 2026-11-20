using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Trax.Samples.StateMachine.E2E.Utilities;

/// <summary>
/// Posts to the sample's <c>/trax/graphql</c> endpoint: a persisted id and its variables, as the page sends them, or a
/// document as text, which the host refuses.
/// </summary>
public class GraphQLClient(HttpClient httpClient)
{
    /// <summary>Sends <paramref name="query"/> as text.</summary>
    public Task<GraphQLResponse> SendAsync(string query, Caller caller, object? variables = null) =>
        PostAsync(new { query, variables }, caller);

    /// <summary>Sends the persisted document <paramref name="id"/> with <paramref name="variables"/>.</summary>
    public Task<GraphQLResponse> SendIdAsync(string id, Caller caller, object variables) =>
        PostAsync(new { id, variables }, caller);

    private async Task<GraphQLResponse> PostAsync(object body, Caller caller)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(body),
        };
        caller.Apply(request);

        using var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(
                $"Empty response body from the GraphQL endpoint (HTTP {(int)response.StatusCode})."
            );

        return new GraphQLResponse(
            JsonSerializer.Deserialize<JsonElement>(text),
            response.StatusCode
        );
    }
}
