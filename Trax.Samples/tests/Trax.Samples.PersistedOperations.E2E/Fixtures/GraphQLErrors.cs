using System.Text.Json;

namespace Trax.Samples.PersistedOperations.E2E.Fixtures;

/// <summary>Reads which error a GraphQL response carries.</summary>
public static class GraphQLErrors
{
    /// <summary>
    /// HotChocolate's code for a persisted operation id the store does not serve: unknown, or
    /// deactivated.
    /// </summary>
    public const string UnknownPersistedOperation = "HC0020";

    /// <summary>The first error's <c>extensions.code</c>, or <c>null</c> when there is none.</summary>
    public static string? FirstCode(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("errors", out var errors)
        && errors.GetArrayLength() > 0
        && errors[0].TryGetProperty("extensions", out var extensions)
        && extensions.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;

    /// <summary>The first error's <c>extensions.code</c> in a raw response body.</summary>
    public static string? FirstCode(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return FirstCode(doc);
    }
}
