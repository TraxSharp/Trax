using System.Text.Json;
using GraphQL.Client.Abstractions.Websocket;
using GraphQL.Client.Http;

namespace Trax.Api.GraphQL.Client;

public class GraphQLClientConfiguration : IGraphQLClientConfiguration, IDisposable
{
    private bool _disposed;

    public GraphQLClientConfiguration(
        Uri baseAddress,
        IGraphQLWebsocketJsonSerializer jsonSerializer,
        GraphQLHttpClientOptions graphQLHttpClientOptions,
        JsonSerializerOptions jsonSerializerOptions,
        bool disposeHttpClient,
        bool removeSubscriptionsFromSchema,
        ResponseStrictness responseStrictness,
        HttpClient httpClient
    )
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentNullException.ThrowIfNull(jsonSerializer);
        ArgumentNullException.ThrowIfNull(graphQLHttpClientOptions);
        ArgumentNullException.ThrowIfNull(jsonSerializerOptions);
        ArgumentNullException.ThrowIfNull(httpClient);

        BaseAddress = baseAddress;
        HttpClient = httpClient;
        HttpClient.BaseAddress = baseAddress;

        JsonSerializerOptions = jsonSerializerOptions;
        GraphQLClientOptions = graphQLHttpClientOptions;
        WebsocketJsonSerializer = jsonSerializer;
        DisposeHttpClient = disposeHttpClient;
        RemoveSubscriptionsFromSchema = removeSubscriptionsFromSchema;
        ResponseStrictness = responseStrictness;

        GraphQLHttpClient = new GraphQLHttpClient(
            serializer: jsonSerializer,
            options: graphQLHttpClientOptions,
            httpClient: httpClient
        );
    }

    public Uri BaseAddress { get; }
    public HttpClient HttpClient { get; }
    public GraphQLHttpClient GraphQLHttpClient { get; }
    public IGraphQLWebsocketJsonSerializer WebsocketJsonSerializer { get; }
    public JsonSerializerOptions JsonSerializerOptions { get; }
    public GraphQLHttpClientOptions GraphQLClientOptions { get; }
    public bool DisposeHttpClient { get; }
    public bool RemoveSubscriptionsFromSchema { get; }
    public ResponseStrictness ResponseStrictness { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        GraphQLHttpClient.Dispose();
        if (DisposeHttpClient)
            HttpClient.Dispose();

        GC.SuppressFinalize(this);
    }
}
