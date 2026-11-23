using System.Text.Json.Nodes;

namespace Trax.Client;

/// <summary>One error of a GraphQL answer.</summary>
/// <param name="Message">What the host said.</param>
/// <param name="Code">Its <c>extensions.code</c> (<c>CLIENT_OUTDATED</c>, <c>AUTH_NOT_AUTHORIZED</c>), when it has one.</param>
/// <param name="Path">The path of the field it belongs to, when it belongs to one.</param>
/// <param name="Extensions">Its <c>extensions</c> as sent.</param>
public sealed record TraxGraphQLError(
    string Message,
    string? Code,
    JsonArray? Path,
    JsonObject? Extensions
)
{
    internal static IReadOnlyList<TraxGraphQLError> ListOf(JsonNode? errors) =>
        errors is JsonArray array ? array.Select(OfNode).ToList() : [];

    private static TraxGraphQLError OfNode(JsonNode? node)
    {
        var error = node as JsonObject;
        var extensions = error?["extensions"] as JsonObject;
        return new TraxGraphQLError(
            Text(error?["message"]) ?? "The host answered with an error.",
            Text(extensions?["code"]),
            error?["path"]?.DeepClone() as JsonArray,
            extensions?.DeepClone() as JsonObject
        );
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
