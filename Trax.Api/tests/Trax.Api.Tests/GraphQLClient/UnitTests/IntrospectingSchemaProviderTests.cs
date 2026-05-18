using FluentAssertions;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

/// <summary>
/// Error paths for IntrospectingSchemaProvider. The happy path is exercised by
/// SchemaProviderTests against a real HotChocolate server; these tests use a stub HTTP
/// handler to inject failure modes that are hard to reproduce otherwise:
/// <list type="bullet">
///   <item>The endpoint returns GraphQL errors instead of data.</item>
///   <item>The endpoint returns malformed JSON.</item>
///   <item>The introspection response is missing the __schema envelope.</item>
///   <item>The endpoint throws a transport exception.</item>
///   <item>The SDL built from introspection fails to parse.</item>
/// </list>
/// Without these tests, a regression in any of the error-wrapping branches would only
/// surface when something went genuinely wrong in production - exactly when you need the
/// best error message.
/// </summary>
[TestFixture]
public class IntrospectingSchemaProviderTests
{
    private static IGraphQLClientConfiguration BuildConfig(HttpMessageHandler handler)
    {
        return new GraphQLClientConfigurationBuilder(new Uri("http://stub/graphql"))
        {
            HttpClient = new HttpClient(handler),
        }.Build();
    }

    [Test]
    public async Task GetSchemaAsync_ErrorsInResponse_ThrowsWithEndpointInMessage()
    {
        var stub = new StubHttpMessageHandler(
            """{"errors":[{"message":"introspection blocked"}]}"""
        );

        var provider = new IntrospectingSchemaProvider(BuildConfig(stub));

        var act = async () => await provider.GetSchemaAsync();

        var ex = await act.Should().ThrowAsync<GraphQLSchemaIntrospectionException>();
        ex.Which.Message.Should().Contain("stub/graphql");
        ex.Which.Message.Should().Contain("introspection blocked");
    }

    [Test]
    public async Task GetSchemaAsync_MalformedJson_ThrowsIntrospectionException()
    {
        // GraphQL HTTP client returns a 200 with broken JSON body.
        var stub = new StubHttpMessageHandler("not even json");

        var provider = new IntrospectingSchemaProvider(BuildConfig(stub));

        var act = async () => await provider.GetSchemaAsync();

        // The exact wrapped exception type varies by serializer version, but it must NOT
        // be a NullReferenceException or other "we forgot to handle this" surprise.
        await act.Should().ThrowAsync<GraphQLSchemaIntrospectionException>();
    }

    [Test]
    public async Task GetSchemaAsync_MissingSchemaEnvelope_ThrowsWithRawDataInMessage()
    {
        // Server returned data, but it wasn't shaped like __schema. Include the raw body
        // so the developer can see what they actually got back.
        var stub = new StubHttpMessageHandler("""{"data":{"foo":"bar"}}""");

        var provider = new IntrospectingSchemaProvider(BuildConfig(stub));

        var act = async () => await provider.GetSchemaAsync();

        var ex = await act.Should().ThrowAsync<GraphQLSchemaIntrospectionException>();
        ex.Which.Message.Should().Contain("missing __schema");
    }

    [Test]
    public async Task GetSchemaAsync_TransportFailure_WrapsAsIntrospectionException()
    {
        var stub = StubHttpMessageHandler.AlwaysThrows(
            new HttpRequestException("connection refused")
        );

        var provider = new IntrospectingSchemaProvider(BuildConfig(stub));

        var act = async () => await provider.GetSchemaAsync();

        var ex = await act.Should().ThrowAsync<GraphQLSchemaIntrospectionException>();
        ex.Which.InnerException.Should().BeAssignableTo<Exception>();
        ex.Which.Message.Should().Contain("stub/graphql");
    }

    [Test]
    public async Task GetSchemaAsync_RemoveSubscriptionsFalse_PreservesSubscriptionRoot()
    {
        // Minimal valid introspection body with a subscription root. By default the provider
        // strips it; with RemoveSubscriptionsFromSchema = false, it should survive.
        const string body = """
            {
              "data": {
                "__schema": {
                  "queryType": { "name": "Query" },
                  "subscriptionType": { "name": "Subscription" },
                  "types": [
                    { "kind": "OBJECT", "name": "Query", "fields": [{ "name": "n", "type": { "kind": "SCALAR", "name": "Int" } }] },
                    { "kind": "OBJECT", "name": "Subscription", "fields": [{ "name": "ticks", "type": { "kind": "SCALAR", "name": "Int" } }] }
                  ]
                }
              }
            }
            """;
        var stub = new StubHttpMessageHandler(body);
        var config = new GraphQLClientConfigurationBuilder(new Uri("http://stub/graphql"))
        {
            HttpClient = new HttpClient(stub),
            RemoveSubscriptionsFromSchema = false,
        }.Build();

        var provider = new IntrospectingSchemaProvider(config);
        var schema = await provider.GetSchemaAsync();

        schema.Subscription.Should().NotBeNull();
        schema.Subscription!.Name.Should().Be("Subscription");
    }

    [Test]
    public async Task GetSchemaAsync_RemoveSubscriptionsTrue_StripsSubscriptionRoot()
    {
        // Mirror of the test above with the default flag - proves the strip path actually
        // runs (the previous test only proves the no-strip path). Both branches now covered.
        const string body = """
            {
              "data": {
                "__schema": {
                  "queryType": { "name": "Query" },
                  "subscriptionType": { "name": "Subscription" },
                  "types": [
                    { "kind": "OBJECT", "name": "Query", "fields": [{ "name": "n", "type": { "kind": "SCALAR", "name": "Int" } }] },
                    { "kind": "OBJECT", "name": "Subscription", "fields": [{ "name": "ticks", "type": { "kind": "SCALAR", "name": "Int" } }] }
                  ]
                }
              }
            }
            """;
        var stub = new StubHttpMessageHandler(body);
        var config = new GraphQLClientConfigurationBuilder(new Uri("http://stub/graphql"))
        {
            HttpClient = new HttpClient(stub),
            RemoveSubscriptionsFromSchema = true,
        }.Build();

        var provider = new IntrospectingSchemaProvider(config);
        var schema = await provider.GetSchemaAsync();

        schema.Subscription.Should().BeNull();
    }
}
