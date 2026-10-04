using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Api.Tests.GraphQLClient.UnitTests;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// Every schema provider loads a schema that declares custom scalars, the kind every HotChocolate
/// server has (Trax's own schema declares <c>Any</c>), and every one honours
/// <see cref="IGraphQLClientConfiguration.RemoveSubscriptionsFromSchema"/>. The three providers
/// build the schema through one step, so a request that validates against one validates against
/// the others.
/// </summary>
[TestFixture]
public class CustomScalarSchemaTests
{
    private const string Sdl = """
        schema {
          query: Query
          subscription: Subscription
        }

        scalar Any
        scalar UUID
        scalar URI

        type Query {
          id: UUID!
          link: URI
          payload: Any
          find(id: UUID!, payload: Any): String
        }

        type Subscription {
          ticked: String!
        }
        """;

    private const string ScalarQuery = """
        query { id link payload find(id: "5f2b", payload: { a: [1, "x"] }) }
        """;

    private const string SubscriptionOperation = "subscription { ticked }";

    private string _path = null!;

    [SetUp]
    public async Task SetUp()
    {
        _path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"scalars-{Guid.NewGuid():N}.graphql"
        );
        await File.WriteAllTextAsync(_path, Sdl);
    }

    [TearDown]
    public void TearDown() => File.Delete(_path);

    [Test]
    public async Task FileSchemaProvider_LoadsASchemaWithCustomScalars()
    {
        var validator = new GraphQLClientValidator(new FileSchemaProvider(_path));

        (await validator.ValidateAsync(ScalarQuery))
            .Should()
            .Be(GraphQLParser.AST.OperationType.Query);
    }

    [Test]
    public async Task FileSchemaProvider_RemovesSubscriptionsByDefault()
    {
        var validator = new GraphQLClientValidator(new FileSchemaProvider(_path));

        var act = () => validator.ValidateAsync(SubscriptionOperation);

        await act.Should().ThrowAsync<GraphQLValidationException>();
    }

    [Test]
    public async Task An_operation_whose_root_type_the_schema_lacks_is_refused()
    {
        var validator = new GraphQLClientValidator(new FileSchemaProvider(_path));

        var act = () => validator.ValidateAsync("mutation { id }");

        (await act.Should().ThrowAsync<GraphQLValidationException>())
            .Which.Message.Should()
            .Contain("no mutation type");
    }

    [Test]
    public async Task FileSchemaProvider_KeepsSubscriptionsWhenAskedTo()
    {
        var validator = new GraphQLClientValidator(
            new FileSchemaProvider(_path, removeSubscriptionsFromSchema: false)
        );

        (await validator.ValidateAsync(SubscriptionOperation))
            .Should()
            .Be(GraphQLParser.AST.OperationType.Subscription);
    }

    [Test]
    public async Task FileSchemaProvider_WithNoSchemaDefinition_RemovesTheSubscriptionType()
    {
        await File.WriteAllTextAsync(
            _path,
            """
            scalar UUID
            type Query { id: UUID! }
            type Subscription { ticked: String! }
            """
        );
        var validator = new GraphQLClientValidator(new FileSchemaProvider(_path));

        (await validator.ValidateAsync("{ id }"))
            .Should()
            .Be(GraphQLParser.AST.OperationType.Query);
        var act = () => validator.ValidateAsync(SubscriptionOperation);
        await act.Should().ThrowAsync<GraphQLValidationException>();
    }

    [Test]
    public async Task FileSchemaProvider_WithNoSubscriptionType_LoadsUnchanged()
    {
        await File.WriteAllTextAsync(_path, "schema { query: Query } type Query { n: Int }");
        var validator = new GraphQLClientValidator(new FileSchemaProvider(_path));

        (await validator.ValidateAsync("{ n }")).Should().Be(GraphQLParser.AST.OperationType.Query);
    }

    [Test]
    public async Task An_extension_of_the_subscription_root_is_removed_with_it()
    {
        await File.WriteAllTextAsync(
            _path,
            """
            scalar Any
            type Query { n: Int }
            type Subscription { ticked: String! }
            extend type Subscription { tocked(payload: Any): String! }
            """
        );
        var validator = new GraphQLClientValidator(new FileSchemaProvider(_path));

        (await validator.ValidateAsync("{ n }")).Should().Be(GraphQLParser.AST.OperationType.Query);
    }

    [Test]
    public async Task A_custom_scalar_argument_with_an_object_default_loads()
    {
        await File.WriteAllTextAsync(
            _path,
            """
            scalar JSON
            type Query { search(filter: JSON = { status: "open", tags: ["a"] }, cursor: JSON = null): String }
            """
        );
        var validator = new GraphQLClientValidator(new FileSchemaProvider(_path));

        (await validator.ValidateAsync("{ search }"))
            .Should()
            .Be(GraphQLParser.AST.OperationType.Query);
    }

    [Test]
    public async Task A_kept_subscription_field_validates_but_is_never_executed_by_the_client()
    {
        var schema = await new FileSchemaProvider(
            _path,
            removeSubscriptionsFromSchema: false
        ).GetSchemaAsync();

        var field = schema.Subscription!.Fields.Find("ticked")!;
        var act = async () => await field.StreamResolver!.ResolveAsync(null!);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Test]
    public async Task An_sdl_naming_an_undeclared_type_fails_the_load_not_the_first_validation()
    {
        await File.WriteAllTextAsync(_path, "type Query { thing: Thing }");
        var provider = new FileSchemaProvider(_path);

        await provider
            .Invoking(p => p.GetSchemaAsync())
            .Should()
            .ThrowAsync<GraphQLSchemaIntrospectionException>()
            .WithInnerException(typeof(InvalidOperationException));
    }

    [Test]
    public async Task UseFileSchema_FollowsTheClientsSubscriptionSetting()
    {
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(new Uri("http://localhost/graphql"))
            .Configure(c => c.RemoveSubscriptionsFromSchema = false)
            .UseFileSchema(_path);
        await using var provider = services.BuildServiceProvider();

        var validator = provider.GetRequiredService<IGraphQLClientValidator>();

        (await validator.ValidateAsync(SubscriptionOperation))
            .Should()
            .Be(GraphQLParser.AST.OperationType.Subscription);
    }

    [Test]
    public async Task AssemblySchemaProvider_LoadsHotChocolatesScalars()
    {
        var validator = new GraphQLClientValidator(new AssemblySchemaProvider(ConfigureServer));

        (await validator.ValidateAsync(HotChocolateQuery))
            .Should()
            .Be(GraphQLParser.AST.OperationType.Query);
        var act = () => validator.ValidateAsync(SubscriptionOperation);
        await act.Should().ThrowAsync<GraphQLValidationException>();
    }

    [Test]
    public async Task UseAssemblySchema_FollowsTheClientsSubscriptionSetting()
    {
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(new Uri("http://localhost/graphql"))
            .Configure(c => c.RemoveSubscriptionsFromSchema = false)
            .UseAssemblySchema(ConfigureServer);
        await using var provider = services.BuildServiceProvider();

        var validator = provider.GetRequiredService<IGraphQLClientValidator>();

        (await validator.ValidateAsync(SubscriptionOperation))
            .Should()
            .Be(GraphQLParser.AST.OperationType.Subscription);
    }

    [Test]
    public async Task IntrospectingSchemaProvider_LoadsHotChocolatesScalars()
    {
        var introspection = await IntrospectServerAsync();
        var config = new GraphQLClientConfigurationBuilder(new Uri("http://stub/graphql"))
        {
            HttpClient = new HttpClient(new StubHttpMessageHandler(introspection)),
        }.Build();
        var validator = new GraphQLClientValidator(new IntrospectingSchemaProvider(config));

        (await validator.ValidateAsync(HotChocolateQuery))
            .Should()
            .Be(GraphQLParser.AST.OperationType.Query);
    }

    private const string HotChocolateQuery = """
        query { id link payload at find(id: "5f2b", payload: null) }
        """;

    private static void ConfigureServer(IRequestExecutorBuilder builder) =>
        builder
            .AddQueryType<ScalarQueryType>()
            .AddSubscriptionType(d =>
                d.Name("Subscription").Field("ticked").Type<NonNullType<StringType>>().Resolve("t")
            );

    private static async Task<string> IntrospectServerAsync()
    {
        var services = new ServiceCollection();
        ConfigureServer(services.AddGraphQL());
        await using var provider = services.BuildServiceProvider();
        var executor = await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync();
        var result = await executor.ExecuteAsync(IntrospectingSchemaProvider.IntrospectionQuery);
        return result.ToJson();
    }

    public sealed class ScalarQueryType
    {
        public Guid Id => Guid.Empty;

        public Uri? Link => null;

        [GraphQLType<AnyType>]
        public object? Payload => null;

        public string? Find(Guid id, [GraphQLType<AnyType>] object? payload) => null;

        public DateTimeOffset At => DateTimeOffset.UnixEpoch;
    }
}
