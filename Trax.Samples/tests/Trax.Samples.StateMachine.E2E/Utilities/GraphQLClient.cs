using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Trax.Samples.StateMachine.E2E.Utilities;

/// <summary>Posts GraphQL documents to the sample's <c>/trax/graphql</c> endpoint.</summary>
public class GraphQLClient(HttpClient httpClient)
{
    public async Task<GraphQLResponse> SendAsync(
        string query,
        Caller caller,
        object? variables = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query, variables }),
        };
        caller.Apply(request);

        using var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

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
