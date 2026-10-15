using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Samples.Recovery.E2E.Utilities;

public class GraphQLClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<GraphQLResponse> SendAsync(
        string query,
        string? apiKey = null,
        object? variables = null
    ) => PostAsync(new { query, variables }, apiKey);

    /// <summary>Sends the persisted document <paramref name="id"/>, as the page's generated client does.</summary>
    public Task<GraphQLResponse> SendIdAsync(
        string id,
        string? apiKey = null,
        object? variables = null
    ) => PostAsync(new { id, variables }, apiKey);

    private async Task<GraphQLResponse> PostAsync(object payload, string? apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };

        if (apiKey != null)
            request.Headers.Add("X-Api-Key", apiKey);

        var response = await httpClient.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(body))
        {
            response.EnsureSuccessStatusCode();
            throw new InvalidOperationException("Empty response body from GraphQL endpoint.");
        }

        var json = JsonSerializer.Deserialize<JsonElement>(body, JsonOptions);
        return new GraphQLResponse(json, response.StatusCode);
    }
}

public class GraphQLResponse(JsonElement root, System.Net.HttpStatusCode statusCode)
{
    public JsonElement Root { get; } = root;

    public System.Net.HttpStatusCode StatusCode { get; } = statusCode;

    public bool HasErrors =>
        Root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0;

    public JsonElement Data => Root.GetProperty("data");

    public string? FirstErrorMessage
    {
        get
        {
            if (Root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
                return errors[0].GetProperty("message").GetString();
            return null;
        }
    }

    public JsonElement GetData(params string[] path)
    {
        var current = Data;
        foreach (var segment in path)
            current = current.GetProperty(segment);
        return current;
    }
}
