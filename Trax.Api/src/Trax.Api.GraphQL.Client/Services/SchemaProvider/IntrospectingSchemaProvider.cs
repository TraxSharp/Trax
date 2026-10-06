using System.Text.Json;
using System.Text.Json.Serialization;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Types;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Fetches the schema from the configured endpoint via introspection on first call, then
/// caches it for the lifetime of the provider; a failed fetch is not cached and the next call
/// tries again. A Trax server allows introspection only in Development unless its host admits
/// this client, so against a production Trax server use <see cref="FileSchemaProvider"/> or the
/// in-process provider from <c>Trax.Api.GraphQL.Client.Trax</c>. The introspection JSON is parsed into a
/// local POCO model and reprinted as SDL so the validator can build a graphql-dotnet
/// <see cref="ISchema"/> from it.
/// </summary>
public class IntrospectingSchemaProvider : ISchemaProvider
{
    /// <summary>
    /// Standard GraphQL introspection query. Spec-defined and stable; the server response
    /// shape is part of the GraphQL specification, not any particular library.
    /// </summary>
    internal const string IntrospectionQuery = """
        query IntrospectionQuery {
          __schema {
            queryType { name }
            mutationType { name }
            subscriptionType { name }
            types {
              kind
              name
              fields(includeDeprecated: true) {
                name
                args { ...InputValue }
                type { ...TypeRef }
                isDeprecated
                deprecationReason
              }
              inputFields { ...InputValue }
              interfaces { ...TypeRef }
              enumValues(includeDeprecated: true) {
                name
                isDeprecated
                deprecationReason
              }
              possibleTypes { ...TypeRef }
            }
            directives {
              name
              locations
              args { ...InputValue }
            }
          }
        }
        fragment InputValue on __InputValue {
          name
          type { ...TypeRef }
          defaultValue
        }
        fragment TypeRef on __Type {
          kind
          name
          ofType {
            kind
            name
            ofType {
              kind
              name
              ofType {
                kind
                name
                ofType {
                  kind
                  name
                  ofType {
                    kind
                    name
                    ofType {
                      kind
                      name
                      ofType { kind name }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    private static readonly JsonSerializerOptions IntrospectionParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IGraphQLClientConfiguration _configuration;
    private readonly RetryingAsyncLazy<ISchema> _schema;

    /// <summary>Creates a provider that introspects the endpoint in <paramref name="configuration"/> when the schema is first requested.</summary>
    /// <param name="configuration">Supplies the endpoint, HTTP client and subscription setting.</param>
    public IntrospectingSchemaProvider(IGraphQLClientConfiguration configuration)
        : this(configuration, TimeProvider.System) { }

    internal IntrospectingSchemaProvider(
        IGraphQLClientConfiguration configuration,
        TimeProvider time
    )
    {
        _configuration = configuration;
        _schema = new RetryingAsyncLazy<ISchema>(LoadSchemaAsync, time);
    }

    /// <summary>
    /// Returns the schema, loading it on the first call and sharing the result. A load that
    /// fails is not kept: it is loaded again once a capped, jittered backoff has passed, and a call
    /// before then gets the failure. <paramref name="cancellationToken"/>
    /// cancels this caller's wait, not a load other callers share.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for the schema.</param>
    public Task<ISchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
        _schema.GetValueAsync(cancellationToken);

    private async Task<ISchema> LoadSchemaAsync()
    {
        GraphQLResponse<JsonElement> response;
        try
        {
            var request = new GraphQLHttpRequest(IntrospectionQuery);
            response = await _configuration
                .GraphQLHttpClient.SendQueryAsync<JsonElement>(request)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not GraphQLSchemaIntrospectionException)
        {
            throw new GraphQLSchemaIntrospectionException(
                $"Failed to introspect schema at {_configuration.BaseAddress}.",
                ex
            );
        }

        if (response.Errors is { Length: > 0 })
        {
            throw new GraphQLSchemaIntrospectionException(
                $"Introspection at {_configuration.BaseAddress} returned errors: "
                    + string.Join("; ", response.Errors.Select(e => e.Message))
            );
        }

        IntrospectionRoot? parsed;
        try
        {
            parsed = response.Data.Deserialize<IntrospectionRoot>(IntrospectionParseOptions);
        }
        catch (JsonException ex)
        {
            throw new GraphQLSchemaIntrospectionException(
                "Introspection response was not valid JSON for the introspection schema shape.",
                ex
            );
        }

        if (parsed?.__Schema is null)
        {
            throw new GraphQLSchemaIntrospectionException(
                $"Introspection response missing __schema data. Raw: {response.Data.GetRawText()}"
            );
        }

        if (_configuration.RemoveSubscriptionsFromSchema)
            parsed.__Schema.SubscriptionType = null;

        var sdl = IntrospectionSdlBuilder.Build(parsed.__Schema);

        try
        {
            // The subscription type is already gone, so the SDL is built as it is. The custom
            // scalars it declares are backed by permissive scalars there, as for the other
            // providers.
            return SdlSchema.Build(sdl, removeSubscriptions: false);
        }
        catch (Exception ex)
        {
            throw new GraphQLSchemaIntrospectionException(
                $"Failed to build schema from introspection-derived SDL. Generated SDL:\n{sdl}",
                ex
            );
        }
    }
}
