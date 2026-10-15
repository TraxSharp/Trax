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

        using var response = await httpClient.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

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

public class GraphQLResponse(JsonElement root, HttpStatusCode statusCode)
{
    public JsonElement Root { get; } = root;

    public HttpStatusCode StatusCode { get; } = statusCode;

    public string Raw => Root.GetRawText();

    public bool HasErrors =>
        Root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0;

    /// <summary>True when every error is Trax's generic refusal, <c>TRAX_AUTHORIZATION</c>.</summary>
    public bool IsRefused =>
        HasErrors
        && Root.GetProperty("errors")
            .EnumerateArray()
            .All(e =>
                e.TryGetProperty("extensions", out var ext)
                && ext.TryGetProperty("code", out var code)
                && code.GetString() == "TRAX_AUTHORIZATION"
                && e.GetProperty("message").GetString() == "Not authorized."
            );

    public JsonElement GetData(params string[] path)
    {
        var current = Root.GetProperty("data");
        foreach (var segment in path)
            current = current.GetProperty(segment);
        return current;
    }
}
