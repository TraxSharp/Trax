using System.Text.Json;
using System.Text.Json.Serialization;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Types;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Fetches the schema from the configured endpoint via introspection on first call, then
/// caches it for the lifetime of the provider. The introspection JSON is parsed into a
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
    private readonly Lazy<Task<ISchema>> _schema;

    public IntrospectingSchemaProvider(IGraphQLClientConfiguration configuration)
    {
        _configuration = configuration;
        _schema = new Lazy<Task<ISchema>>(
            LoadSchemaAsync,
            LazyThreadSafetyMode.ExecutionAndPublication
        );
    }

    public Task<ISchema> GetSchemaAsync(CancellationToken cancellationToken = default) =>
        _schema.Value;

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
                $"Failed to introspect schema at {_configuration.HttpClient.BaseAddress}.",
                ex
            );
        }

        if (response.Errors is { Length: > 0 })
        {
            throw new GraphQLSchemaIntrospectionException(
                $"Introspection at {_configuration.HttpClient.BaseAddress} returned errors: "
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
            return Schema.For(sdl);
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
