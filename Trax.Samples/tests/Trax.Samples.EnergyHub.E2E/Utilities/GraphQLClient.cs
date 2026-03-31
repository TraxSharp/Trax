using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Samples.EnergyHub.E2E.Utilities;

public class GraphQLClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<GraphQLResponse> SendAsync(string query, object? variables = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query, variables }, options: JsonOptions),
        };

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

    public string? FirstErrorMessage =>
        Root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0
            ? errors[0].GetProperty("message").GetString()
            : null;

    public JsonElement GetData(params string[] path)
    {
        var current = Data;
        foreach (var segment in path)
            current = current.GetProperty(segment);
        return current;
    }
}
