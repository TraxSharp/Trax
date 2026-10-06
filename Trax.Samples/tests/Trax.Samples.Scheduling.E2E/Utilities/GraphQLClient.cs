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

        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return new GraphQLResponse(
            JsonDocument.Parse(body).RootElement.Clone(),
            response.StatusCode,
            body
        );
    }
}

public sealed record GraphQLResponse(JsonElement Root, HttpStatusCode StatusCode, string Body)
{
    public bool HasErrors =>
        Root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0;

    public string? FirstErrorCode =>
        HasErrors
        && Root.GetProperty("errors")[0].TryGetProperty("extensions", out var ext)
        && ext.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;

    /// <summary>Walks <c>data</c> down <paramref name="path"/>, failing with the body when there were errors.</summary>
    public JsonElement Data(params string[] path)
    {
        HasErrors.Should().BeFalse($"the operation should succeed, but returned {Body}");
        var current = Root.GetProperty("data");
        foreach (var segment in path)
            current = current.GetProperty(segment);
        return current;
    }
}
