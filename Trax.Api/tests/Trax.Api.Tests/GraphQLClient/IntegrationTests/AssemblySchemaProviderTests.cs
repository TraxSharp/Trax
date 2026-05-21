using FluentAssertions;
using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Api.Tests.GraphQLClient.Fixtures;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// AssemblySchemaProvider is meant to replace the introspection round-trip with an in-process
/// schema build from the server's DLL. These tests pin down: it produces a usable schema, the
/// validator can use it, queries that pass introspection-mode validation also pass
/// assembly-mode validation (cross-provider equivalence), and a malformed configurator
/// surfaces a clear error.
/// </summary>
[TestFixture]
public class AssemblySchemaProviderTests
{
    private static void ConfigureTestSchema(IRequestExecutorBuilder b) =>
        b.AddQueryType<TestQuery>().AddMutationType<TestMutation>().DisableIntrospection(false);

    [Test]
    public async Task GetSchemaAsync_BuildsFromConfigurator()
    {
        var provider = new AssemblySchemaProvider(ConfigureTestSchema);

        var schema = await provider.GetSchemaAsync();

        schema.Should().NotBeNull();
        schema.Query.Should().NotBeNull();
        schema.Mutation.Should().NotBeNull();
    }

    [Test]
    public async Task GetSchemaAsync_CalledTwice_ReturnsSameInstance()
    {
        var provider = new AssemblySchemaProvider(ConfigureTestSchema);

        var first = await provider.GetSchemaAsync();
        var second = await provider.GetSchemaAsync();

        ReferenceEquals(first, second).Should().BeTrue();
    }

    [Test]
    public async Task ValidatorBackedByAssemblyProvider_ValidatesQuerySuccessfully()
    {
        var provider = new AssemblySchemaProvider(ConfigureTestSchema);
        var validator = new GraphQLClientValidator(provider);

        var op = await validator.ValidateAsync("query { allItems { id name } }");

        op.Should().Be(GraphQLParser.AST.OperationType.Query);
    }

    [Test]
    public async Task ValidatorBackedByAssemblyProvider_RejectsDriftedQuery()
    {
        var provider = new AssemblySchemaProvider(ConfigureTestSchema);
        var validator = new GraphQLClientValidator(provider);

        var act = async () =>
            await validator.ValidateAsync("query { allItems { id totally_made_up } }");

        await act.Should().ThrowAsync<GraphQLValidationException>();
    }

    [Test]
    public async Task ValidatorBackedByAssemblyProvider_ValidatesNestedPathQuery()
    {
        // The Path-emitting generator produces multi-level selections like
        // `discover { netsuite { typedCustomer(...) { ... } } }`. The validator must
        // walk those nested types through the assembly-built schema correctly. Without
        // this test, a regression could pass the introspecting-provider tests (which
        // also nest) while breaking only the assembly-built variant (different code
        // paths inside HotChocolate's schema-builder vs introspection-deserializer).
        var provider = new AssemblySchemaProvider(ConfigureTestSchema);
        var validator = new GraphQLClientValidator(provider);

        var query = new GetNestedCustomerByEmailRequest { Email = "x" }.Query;
        var op = await validator.ValidateAsync(query);

        op.Should().Be(GraphQLParser.AST.OperationType.Query);
    }

    [Test]
    public async Task AssemblySchemaClient_RunsNestedPathTypedRequest()
    {
        // End-to-end through UseAssemblySchema: the schema is built in-process from the
        // same delegate the server uses. If the generator emits nested selections that
        // the assembly-built schema can't validate (or the response doesn't shape the
        // way the extractor walks), this test fails. Proves Path works against the
        // strongest schema source the client supports, not just introspection.
        using var fixture = new GraphQLTestServerFixture();
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(fixture.BaseAddress)
            .ConfigureHttpClient(fixture.CreateHttpClient())
            .UseAssemblySchema(ConfigureTestSchema);

        await using var sp = services.BuildServiceProvider();
        var executor = sp.GetRequiredService<IGraphQLClientExecutor>();

        var result = await executor.Run(new GetNestedCustomerByEmailRequest { Email = "Aragorn" });

        result.Should().NotBeNull();
        result!.Id.Should().Be("player-1");
        result.Name.Should().Be("Aragorn");
    }

    [Test]
    public async Task CrossProvider_SameQueryValidatesIdenticallyThroughBoth()
    {
        // Critical invariant: schema providers are interchangeable. A query that validates
        // via introspection must validate identically via the in-process assembly build.
        // If they diverge, the AssemblySchemaProvider is silently using a different schema
        // than the running server - a worse failure mode than a clean introspection error.
        using var fixture = new GraphQLTestServerFixture();
        var introspectingConfig = new GraphQLClientConfigurationBuilder(fixture.BaseAddress)
        {
            HttpClient = fixture.CreateHttpClient(),
        }.Build();

        var introspecting = new IntrospectingSchemaProvider(introspectingConfig);
        var assembly = new AssemblySchemaProvider(ConfigureTestSchema);

        var v1 = new GraphQLClientValidator(introspecting);
        var v2 = new GraphQLClientValidator(assembly);

        const string query =
            "query Q($id: String!) { player(id: $id) { id name level rank guild { id name } } }";
        var op1 = await v1.ValidateAsync(query);
        var op2 = await v2.ValidateAsync(query);

        op2.Should().Be(op1);
    }

    [Test]
    public void Constructor_NullConfigurator_Throws()
    {
        var act = () => new AssemblySchemaProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Builder_UseAssemblySchema_ReplacesSchemaProvider()
    {
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(new Uri("http://localhost/graphql"))
            .UseAssemblySchema(ConfigureTestSchema);

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<ISchemaProvider>();

        provider.Should().BeOfType<AssemblySchemaProvider>();
    }

    [Test]
    public void Builder_NoUseSchemaCall_LeavesIntrospectingDefault()
    {
        var services = new ServiceCollection();
        services.AddTraxGraphQLClient(new Uri("http://localhost/graphql"));

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<ISchemaProvider>();

        provider.Should().BeOfType<IntrospectingSchemaProvider>();
    }

    [Test]
    public void Builder_UseFileSchema_RegistersFileSchemaProvider()
    {
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(new Uri("http://localhost/graphql"))
            .UseFileSchema("/tmp/schema.graphql");

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<ISchemaProvider>();

        provider.Should().BeOfType<FileSchemaProvider>();
    }

    [Test]
    public void Builder_UseIntrospection_RegistersIntrospectingProvider()
    {
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(new Uri("http://localhost/graphql"))
            .UseFileSchema("/tmp/x.graphql")
            .UseIntrospection();

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<ISchemaProvider>();

        provider.Should().BeOfType<IntrospectingSchemaProvider>();
    }
}
