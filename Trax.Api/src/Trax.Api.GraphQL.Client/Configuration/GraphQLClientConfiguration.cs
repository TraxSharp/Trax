using System.Text.Json;
using GraphQL.Client.Abstractions.Websocket;
using GraphQL.Client.Http;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// The default <see cref="IGraphQLClientConfiguration"/>, produced by
/// <see cref="GraphQLClientConfigurationBuilder.Build"/>. Infrastructure used by the client
/// registration; not intended to be constructed directly. Depend on
/// <see cref="IGraphQLClientConfiguration"/> instead.
/// </summary>
internal class GraphQLClientConfiguration : IGraphQLClientConfiguration, IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Creates the configuration and the GraphQL.Client HTTP client over <paramref name="httpClient"/>,
    /// overwriting that client's <c>BaseAddress</c> with <paramref name="baseAddress"/>.
    /// </summary>
    /// <param name="baseAddress">The GraphQL endpoint.</param>
    /// <param name="jsonSerializer">The transport serializer.</param>
    /// <param name="graphQLHttpClientOptions">The GraphQL.Client options.</param>
    /// <param name="jsonSerializerOptions">The options responses are deserialized with.</param>
    /// <param name="disposeHttpClient">Whether <see cref="Dispose"/> also disposes <paramref name="httpClient"/>.</param>
    /// <param name="removeSubscriptionsFromSchema">Whether introspection drops the subscription type.</param>
    /// <param name="responseStrictness">How strictly responses are checked.</param>
    /// <param name="httpClient">The HTTP client requests go through.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is <c>null</c>.</exception>
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

    /// <inheritdoc/>
    public Uri BaseAddress { get; }

    /// <inheritdoc/>
    public HttpClient HttpClient { get; }

    /// <inheritdoc/>
    public GraphQLHttpClient GraphQLHttpClient { get; }

    /// <inheritdoc/>
    public IGraphQLWebsocketJsonSerializer WebsocketJsonSerializer { get; }

    /// <inheritdoc/>
    public JsonSerializerOptions JsonSerializerOptions { get; }

    /// <inheritdoc/>
    public GraphQLHttpClientOptions GraphQLClientOptions { get; }

    /// <inheritdoc/>
    public bool DisposeHttpClient { get; }

    /// <inheritdoc/>
    public bool RemoveSubscriptionsFromSchema { get; }

    /// <inheritdoc/>
    public ResponseStrictness ResponseStrictness { get; }

    /// <summary>
    /// Disposes the GraphQL.Client HTTP client, and <see cref="HttpClient"/> too when
    /// <see cref="DisposeHttpClient"/> is set. Safe to call more than once.
    /// </summary>
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
