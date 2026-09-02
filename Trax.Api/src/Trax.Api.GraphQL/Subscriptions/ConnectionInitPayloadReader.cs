using System.Text.Json;
using HotChocolate.AspNetCore.Subscriptions.Protocols;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Deserializes the <c>connection_init</c> payload of a GraphQL-over-WebSocket frame
/// into a typed record.
/// </summary>
/// <remarks>
/// HotChocolate hands the payload over as raw JSON. Clients send the well-known keys in
/// camelCase (<c>authToken</c>, <c>apiKey</c>, <c>bearer</c>), so the reader uses the web
/// defaults: camelCase naming plus case-insensitive matching, which also accepts the
/// PascalCase spellings some hand-rolled clients emit.
/// <para>
/// A payload that is absent, null, or not valid JSON for the target shape yields
/// <c>null</c> rather than throwing — the interceptors treat a missing payload and a
/// malformed one identically, rejecting the connection with the same message.
/// </para>
/// </remarks>
internal static class ConnectionInitPayloadReader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Reads the payload as <typeparamref name="T"/>, or returns <c>null</c> when the
    /// frame carries no payload or the payload does not deserialize.
    /// </summary>
    public static T? TryRead<T>(IOperationMessagePayload payload)
        where T : class
    {
        try
        {
            return payload.Payload?.Deserialize<T>(Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
