using System.Net;
using System.Net.WebSockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Client;

/// <summary>The server ended a subscription with GraphQL errors.</summary>
public sealed class SubscriptionErrorException : Exception
{
    /// <summary>Builds the exception from the errors the server sent.</summary>
    public SubscriptionErrorException(JsonArray? errors)
        : base(
            errors is { Count: > 0 }
            && errors[0] is JsonObject first
            && first["message"] is JsonValue message
            && message.TryGetValue<string>(out var text)
                ? text
                : "The server ended the subscription with an error."
        ) => Errors = errors ?? [];

    /// <summary>The errors as the server sent them.</summary>
    public JsonArray Errors { get; }
}
