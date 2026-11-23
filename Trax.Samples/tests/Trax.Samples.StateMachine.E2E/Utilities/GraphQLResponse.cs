using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Trax.Samples.StateMachine.E2E.Utilities;

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
                && string.Equals(code.GetString(), "TRAX_AUTHORIZATION", StringComparison.Ordinal)
                && string.Equals(
                    e.GetProperty("message").GetString(),
                    "Not authorized.",
                    StringComparison.Ordinal
                )
            );

    public JsonElement GetData(params string[] path)
    {
        var current = Root.GetProperty("data");
        foreach (var segment in path)
            current = current.GetProperty(segment);
        return current;
    }
}
