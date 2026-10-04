using System.Text.Json;
using System.Text.Json.Serialization;
using GraphQL.Client.Abstractions.Websocket;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using Trax.Api.GraphQL.Client.Utils.Converters;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// The mutable options a client kernel is built from. <c>AddTraxGraphQLClient</c> creates one and
/// <see cref="TraxGraphQLClientBuilder"/> sets it; <see cref="TraxGraphQLClientBuilder.Configure"/>
/// exposes it for options without a dedicated method. The configuration is built from it when the
/// kernel is first resolved, so changes made after that are not seen.
/// </summary>
public class GraphQLClientConfigurationBuilder
{
    private readonly Uri _baseAddress;

    /// <summary>Creates a builder with the default options for the given endpoint.</summary>
    /// <param name="baseAddress">The GraphQL endpoint.</param>
    /// <exception cref="ArgumentNullException"><paramref name="baseAddress"/> is <c>null</c>.</exception>
    public GraphQLClientConfigurationBuilder(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        _baseAddress = baseAddress;
    }

    /// <summary>
    /// Builds a configuration from the current options. Each call creates a new GraphQL.Client HTTP
    /// client over the same <see cref="HttpClient"/>. With no <see cref="HttpClient"/> set, it
    /// creates a long-lived one whose pooled connections are replaced every
    /// <see cref="PooledConnectionLifetime"/>, so DNS changes are seen, and disposes it with the
    /// configuration. A client registered with <c>AddTraxGraphQLClient</c> gets its
    /// <see cref="HttpClient"/> from <c>IHttpClientFactory</c> instead.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="GraphQLClientOptions"/> names an endpoint other than the client's address.</exception>
    public IGraphQLClientConfiguration Build() => Build(httpClientFactory: null);

    /// <summary>
    /// How long a pooled connection of an <see cref="HttpClient"/> the client creates is reused
    /// before it is replaced: two minutes, the value Microsoft's HttpClient guidance uses.
    /// </summary>
    internal static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Builds the configuration over <see cref="HttpClient"/> when one was supplied, otherwise
    /// over the client <paramref name="httpClientFactory"/> creates, otherwise over a long-lived
    /// client of its own.
    /// </summary>
    internal IGraphQLClientConfiguration Build(Func<HttpClient>? httpClientFactory)
    {
        var supplied = HttpClient;
        var owned = supplied is null && httpClientFactory is null;
        var client =
            supplied
            ?? httpClientFactory?.Invoke()
            ?? new HttpClient(
                new SocketsHttpHandler { PooledConnectionLifetime = PooledConnectionLifetime }
            );

        return new GraphQLClientConfiguration(
            _baseAddress,
            WebsocketJsonSerializer,
            GraphQLClientOptions,
            JsonSerializerOptions,
            DisposeHttpClient || owned,
            RemoveSubscriptionsFromSchema,
            ResponseStrictness,
            client
        );
    }

    /// <summary>The transport serializer. Defaults to GraphQL.Client's System.Text.Json serializer.</summary>
    public IGraphQLWebsocketJsonSerializer WebsocketJsonSerializer { get; set; } =
        new SystemTextJsonSerializer();

    /// <summary>
    /// The options responses are deserialized with. Defaults to case-insensitive property names, enums
    /// as SNAKE_CASE_UPPER strings (the GraphQL enum convention), and <c>DateOnly</c> support.
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } =
        new()
        {
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper),
                new DateOnlyConverter(),
            },
        };

    /// <summary>The GraphQL.Client options. Defaults to a new, default instance.</summary>
    public GraphQLHttpClientOptions GraphQLClientOptions { get; set; } = new();

    /// <summary>
    /// An HTTP client to send requests through instead of the default, or <c>null</c> (the
    /// default) for the one <c>IHttpClientFactory</c> creates under this client's name. A
    /// supplied client is not changed: requests are sent to the client's address whatever its
    /// <c>BaseAddress</c>, so one client can serve several GraphQL clients. Prefer adding handlers
    /// through <see cref="TraxGraphQLClientBuilder.HttpClientBuilder"/>.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>Whether disposing the configuration also disposes a supplied <see cref="HttpClient"/>. <c>false</c> by default.</summary>
    public bool DisposeHttpClient { get; set; } = false;

    /// <summary>
    /// Whether the schema's subscription type is dropped before queries are validated against it,
    /// by every schema provider the client builder registers. <c>true</c> by default: the executor
    /// sends queries and mutations only.
    /// </summary>
    public bool RemoveSubscriptionsFromSchema { get; set; } = true;

    /// <summary>
    /// Controls how aggressively the executor checks that the JSON response shape matches
    /// the request's POCO. See <see cref="ResponseStrictness"/>.
    /// </summary>
    public ResponseStrictness ResponseStrictness { get; set; } = ResponseStrictness.Lenient;
}
