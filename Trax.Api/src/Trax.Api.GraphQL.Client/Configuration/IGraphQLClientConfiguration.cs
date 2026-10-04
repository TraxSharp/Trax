using System.Text.Json;
using GraphQL.Client.Abstractions.Websocket;
using GraphQL.Client.Http;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// The settings one GraphQL client kernel runs with: where the server is, the HTTP and JSON
/// plumbing, and how strictly responses are checked. Built once from the options set on
/// <see cref="TraxGraphQLClientBuilder"/> and registered as a singleton (keyed, for a keyed client).
/// </summary>
public interface IGraphQLClientConfiguration
{
    /// <summary>The GraphQL endpoint every request is sent to.</summary>
    Uri BaseAddress { get; }

    /// <summary>
    /// The HTTP client requests go through: the one <c>IHttpClientFactory</c> creates under this
    /// client's name, or the one supplied. Its <c>BaseAddress</c> is not used or changed; requests are
    /// sent to <see cref="BaseAddress"/>.
    /// </summary>
    HttpClient HttpClient { get; }

    /// <summary>The GraphQL.Client HTTP client built over <see cref="HttpClient"/>, used for requests and schema introspection.</summary>
    GraphQLHttpClient GraphQLHttpClient { get; }

    /// <summary>The serializer the GraphQL.Client transport uses for request and response envelopes.</summary>
    IGraphQLWebsocketJsonSerializer WebsocketJsonSerializer { get; }

    /// <summary>The options responses are deserialized into request types with, and that response-shape checks use to name properties.</summary>
    JsonSerializerOptions JsonSerializerOptions { get; }

    /// <summary>The GraphQL.Client options the HTTP client was built with.</summary>
    GraphQLHttpClientOptions GraphQLClientOptions { get; }

    /// <summary>Whether disposing the configuration also disposes <see cref="HttpClient"/>. <c>false</c> by default: the caller owns the client.</summary>
    bool DisposeHttpClient { get; }

    /// <summary>
    /// Whether the schema has its subscription type dropped before queries are validated against it.
    /// Every schema provider the client builder registers honours it: introspection,
    /// <c>UseFileSchema</c> and <c>UseAssemblySchema</c>.
    /// </summary>
    bool RemoveSubscriptionsFromSchema { get; }

    /// <summary>How strictly responses are checked against the request's response type.</summary>
    ResponseStrictness ResponseStrictness { get; }
}
