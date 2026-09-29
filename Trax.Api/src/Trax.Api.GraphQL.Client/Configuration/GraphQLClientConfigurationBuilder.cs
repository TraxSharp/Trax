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
    /// client over the same <see cref="HttpClient"/>.
    /// </summary>
    public IGraphQLClientConfiguration Build() =>
        new GraphQLClientConfiguration(
            _baseAddress,
            WebsocketJsonSerializer,
            GraphQLClientOptions,
            JsonSerializerOptions,
            DisposeHttpClient,
            RemoveSubscriptionsFromSchema,
            ResponseStrictness,
            HttpClient
        );

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
    /// The HTTP client requests go through. Defaults to a new <see cref="System.Net.Http.HttpClient"/>;
    /// replace it to add authentication handlers or timeouts. Its <c>BaseAddress</c> is overwritten.
    /// </summary>
    public HttpClient HttpClient { get; set; } = new();

    /// <summary>Whether disposing the configuration also disposes <see cref="HttpClient"/>. <c>false</c> by default.</summary>
    public bool DisposeHttpClient { get; set; } = false;

    /// <summary>
    /// Has no effect: nothing reads it, and it is not carried into the built configuration. To
    /// validate request types up front, call <c>UseStartupValidation(...)</c> from
    /// <c>Trax.Api.GraphQL.Client.Trax</c>, or
    /// <see cref="GraphQLClientValidatorExtensions.ValidateAssembliesAsync(IGraphQLClientValidator, IEnumerable{System.Reflection.Assembly}, CancellationToken)"/>
    /// yourself.
    /// </summary>
    public bool ValidateAssemblies { get; set; } = false;

    /// <summary>
    /// Subscriptions in the schema require a subscription type on every client query regardless
    /// of intent, so they may be removed from the introspected schema if subscriptions aren't used.
    /// </summary>
    public bool RemoveSubscriptionsFromSchema { get; set; } = true;

    /// <summary>
    /// Controls how aggressively the executor checks that the JSON response shape matches
    /// the request's POCO. See <see cref="ResponseStrictness"/>.
    /// </summary>
    public ResponseStrictness ResponseStrictness { get; set; } = ResponseStrictness.Lenient;
}
