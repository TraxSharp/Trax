using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Samples.ContentShield.E2E.Utilities;

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
