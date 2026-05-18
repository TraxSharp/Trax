using System.Text.Json;
using GraphQL.Client.Abstractions.Websocket;
using GraphQL.Client.Http;

namespace Trax.Api.GraphQL.Client;

public interface IGraphQLClientConfiguration
{
    Uri BaseAddress { get; }

    HttpClient HttpClient { get; }
    GraphQLHttpClient GraphQLHttpClient { get; }

    IGraphQLWebsocketJsonSerializer WebsocketJsonSerializer { get; }
    JsonSerializerOptions JsonSerializerOptions { get; }
    GraphQLHttpClientOptions GraphQLClientOptions { get; }

    bool DisposeHttpClient { get; }
    bool RemoveSubscriptionsFromSchema { get; }
    ResponseStrictness ResponseStrictness { get; }
}
