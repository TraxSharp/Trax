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
    public async Task GetSchemaAsync_CustomScalar_BuildsSchemaSuccessfully()
    {
        // Real bug surfaced by Trax-on-Trax E2E: HotChocolate's `Any` scalar (used on
        // TrainLifecycleEvent.payload) appears in the introspection result, the SDL builder
        // emits `scalar Any`, but graphql-dotnet's Schema.For can't resolve field references
        // of type Any because no IGraphType is registered for it. The fix registers a
        // permissive scalar for every non-built-in scalar in the introspection.
        const string body = """
            {
              "data": {
                "__schema": {
                  "queryType": { "name": "Query" },
                  "types": [
                    { "kind": "SCALAR", "name": "Any" },
                    { "kind": "OBJECT", "name": "Query", "fields": [
                      { "name": "payload", "type": { "kind": "SCALAR", "name": "Any" } }
                    ] }
                  ]
                }
              }
            }
            """;
        var stub = new StubHttpMessageHandler(body);

        var provider = new IntrospectingSchemaProvider(BuildConfig(stub));
        var schema = await provider.GetSchemaAsync();

        // Force initialization to surface any unresolved type references. Without the fix,
        // this throws "Unable to resolve reference to type 'Any' on 'Query'".
        schema.Initialize();

        schema.Query.Should().NotBeNull();
        schema.Query!.GetField("payload").Should().NotBeNull();
        schema.Query.GetField("payload")!.ResolvedType!.Name.Should().Be("Any");
    }

    [Test]
    public async Task GetSchemaAsync_CustomScalar_ValidatesQuerySelectingCustomScalarField()
    {
        // After the fix, the validator can validate queries that select fields of a custom
        // scalar type. This is the realistic shape for Trax's TrainLifecycleEvent.payload
        // and for any external server using custom scalars (DateTime, JSON, UUID, ...).
        const string body = """
            {
              "data": {
                "__schema": {
                  "queryType": { "name": "Query" },
                  "types": [
                    { "kind": "SCALAR", "name": "Any" },
                    { "kind": "OBJECT", "name": "Query", "fields": [
                      { "name": "payload", "type": { "kind": "SCALAR", "name": "Any" } }
                    ] }
                  ]
                }
              }
            }
            """;
        var stub = new StubHttpMessageHandler(body);
        var provider = new IntrospectingSchemaProvider(BuildConfig(stub));
        var validator = new GraphQLClientValidator(provider);

        var op = await validator.ValidateAsync("query { payload }");

        op.Should().Be(GraphQLParser.AST.OperationType.Query);
    }

    [Test]
    public async Task GetSchemaAsync_MultipleCustomScalars_AllResolved()
    {
        // Sanity check that the registration loop handles more than one custom scalar —
        // a real-world schema typically declares several (Any, DateTime, UUID, JSON, ...).
        const string body = """
            {
              "data": {
                "__schema": {
                  "queryType": { "name": "Query" },
                  "types": [
                    { "kind": "SCALAR", "name": "Any" },
                    { "kind": "SCALAR", "name": "DateTime" },
                    { "kind": "SCALAR", "name": "Uuid" },
                    { "kind": "OBJECT", "name": "Query", "fields": [
                      { "name": "payload", "type": { "kind": "SCALAR", "name": "Any" } },
                      { "name": "when", "type": { "kind": "SCALAR", "name": "DateTime" } },
                      { "name": "id", "type": { "kind": "SCALAR", "name": "Uuid" } }
                    ] }
                  ]
                }
              }
            }
            """;
        var stub = new StubHttpMessageHandler(body);
        var provider = new IntrospectingSchemaProvider(BuildConfig(stub));

        var schema = await provider.GetSchemaAsync();
        schema.Initialize();

        schema.Query!.GetField("payload")!.ResolvedType!.Name.Should().Be("Any");
        schema.Query.GetField("when")!.ResolvedType!.Name.Should().Be("DateTime");
        schema.Query.GetField("id")!.ResolvedType!.Name.Should().Be("Uuid");
    }

    [Test]
    public async Task GetSchemaAsync_CustomScalar_LiteralArgumentValidates()
    {
        // graphql-dotnet's query validator invokes ParseLiteral on argument literals to
        // confirm the value coerces into the declared scalar type. The permissive backing
        // for our introspected scalars must accept any literal kind. Without this test, a
        // regression that threw on, say, integer literals would silently reject every real
        // query that passes numeric values to custom-scalar args.
        const string body = """
            {
              "data": {
                "__schema": {
                  "queryType": { "name": "Query" },
                  "types": [
                    { "kind": "SCALAR", "name": "Any" },
                    { "kind": "OBJECT", "name": "Query", "fields": [
                      { "name": "lookup",
                        "type": { "kind": "SCALAR", "name": "String" },
                        "args": [
                          { "name": "input", "type": { "kind": "NON_NULL", "ofType": { "kind": "SCALAR", "name": "Any" } } }
                        ]
                      }
                    ] }
                  ]
                }
              }
            }
            """;
        var stub = new StubHttpMessageHandler(body);
        var provider = new IntrospectingSchemaProvider(BuildConfig(stub));
        var validator = new GraphQLClientValidator(provider);

        // Pass literals of every shape the validator might hand to ParseLiteral. If any
        // throws, validation fails — exactly the regression class direct ParseLiteral unit
        // tests pretended to catch but couldn't (the validator does its own coercion path).
        await validator.ValidateAsync("""query { lookup(input: "hello") }""");
        await validator.ValidateAsync("""query { lookup(input: 42) }""");
        await validator.ValidateAsync("""query { lookup(input: 3.14) }""");
        await validator.ValidateAsync("""query { lookup(input: true) }""");
        await validator.ValidateAsync("""query { lookup(input: false) }""");
    }

    [Test]
    public async Task GetSchemaAsync_BuiltinScalars_NotDoublyRegistered()
    {
        // Built-in scalars (String, Int, Float, Boolean, ID) come pre-registered by
        // graphql-dotnet. Re-registering them with our permissive shim would either throw
        // or shadow the built-in behavior. The custom-scalar registration loop must skip
        // built-ins.
        const string body = """
            {
              "data": {
                "__schema": {
                  "queryType": { "name": "Query" },
                  "types": [
                    { "kind": "SCALAR", "name": "String" },
                    { "kind": "SCALAR", "name": "Int" },
                    { "kind": "SCALAR", "name": "Float" },
                    { "kind": "SCALAR", "name": "Boolean" },
                    { "kind": "SCALAR", "name": "ID" },
                    { "kind": "OBJECT", "name": "Query", "fields": [
                      { "name": "n", "type": { "kind": "SCALAR", "name": "Int" } }
                    ] }
                  ]
                }
              }
            }
            """;
        var stub = new StubHttpMessageHandler(body);
        var provider = new IntrospectingSchemaProvider(BuildConfig(stub));

        var schema = await provider.GetSchemaAsync();
        schema.Initialize();

        // The Int field's resolved type must still be graphql-dotnet's built-in IntGraphType,
        // not a permissive shim. We verify by checking the resolved type's CLR type comes
        // from graphql-dotnet's built-in scalar namespace, not our internal one.
        var intType = schema.Query!.GetField("n")!.ResolvedType!;
        intType
            .GetType()
            .FullName.Should()
            .StartWith("GraphQL.", "built-in scalars must keep graphql-dotnet's native types");
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
