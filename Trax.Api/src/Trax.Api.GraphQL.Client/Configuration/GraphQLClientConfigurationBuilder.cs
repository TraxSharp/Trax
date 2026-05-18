using System.Text.Json;
using System.Text.Json.Serialization;
using GraphQL.Client.Abstractions.Websocket;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using Trax.Api.GraphQL.Client.Utils.Converters;

namespace Trax.Api.GraphQL.Client;

public class GraphQLClientConfigurationBuilder
{
    private readonly Uri _baseAddress;

    public GraphQLClientConfigurationBuilder(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        _baseAddress = baseAddress;
    }

    public GraphQLClientConfiguration Build() =>
        new(
            _baseAddress,
            WebsocketJsonSerializer,
            GraphQLClientOptions,
            JsonSerializerOptions,
            DisposeHttpClient,
            RemoveSubscriptionsFromSchema,
            ResponseStrictness,
            HttpClient
        );

    public IGraphQLWebsocketJsonSerializer WebsocketJsonSerializer { get; set; } =
        new SystemTextJsonSerializer();

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

    public GraphQLHttpClientOptions GraphQLClientOptions { get; set; } = new();

    public HttpClient HttpClient { get; set; } = new();

    public bool DisposeHttpClient { get; set; } = false;

    /// <summary>
    /// If true, run <see cref="GraphQLClientValidatorExtensions.ValidateAssembliesAsync"/> at startup
    /// against the supplied assemblies. This eagerly catches schema-incompatible queries.
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
