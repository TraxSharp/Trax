using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Trax.Samples.Scheduling.E2E.Utilities;

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
