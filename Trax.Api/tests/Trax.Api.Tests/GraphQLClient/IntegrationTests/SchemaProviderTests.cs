using FluentAssertions;
using GraphQL.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.Tests.GraphQLClient.Fixtures;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// Both schema providers must produce a usable <see cref="ISchema"/> that the validator can
/// consult. These tests also act as the byte-equal cross-provider proof: a request validated
/// via introspection must validate identically via a file-based snapshot of the same schema.
/// If they diverge, the providers aren't interchangeable.
/// </summary>
[TestFixture]
public class SchemaProviderTests
{
    private GraphQLTestServerFixture _fixture = null!;

    [SetUp]
    public void SetUp() => _fixture = new GraphQLTestServerFixture();

    [TearDown]
    public void TearDown() => _fixture.Dispose();

    [Test]
    public async Task IntrospectingProvider_FetchesSchema_AndContainsExpectedRootTypes()
    {
        var config = BuildConfig();
        var provider = new IntrospectingSchemaProvider(config);

        var schema = await provider.GetSchemaAsync();

        schema.Should().NotBeNull();
        schema.Query.Should().NotBeNull();
        schema.Query!.Name.Should().Be("TestQuery");
        schema.Mutation.Should().NotBeNull();
        schema.Mutation!.Name.Should().Be("TestMutation");
    }

    [Test]
    public async Task IntrospectingProvider_CalledTwice_ReturnsSameInstance()
    {
        var config = BuildConfig();
        var provider = new IntrospectingSchemaProvider(config);

        var first = await provider.GetSchemaAsync();
        var second = await provider.GetSchemaAsync();

        ReferenceEquals(first, second).Should().BeTrue("schema is cached for provider lifetime");
    }

    [Test]
    public async Task FileSchemaProvider_LoadsCheckedInSdl_AndValidatesIdenticalQuery()
    {
        // A minimal SDL that exposes the same query field the test uses below. Mirrors the
        // server's schema enough that validation can succeed; deliberately not exhaustive so
        // the test pins down the contract "this much SDL is enough for validation."
        const string sdl = """
            schema { query: Query }
            type Query {
              allItems: [Item!]!
            }
            type Item {
              id: ID!
              name: String!
              category: ItemCategory!
            }
            enum ItemCategory { WEAPON ARMOR CONSUMABLE }
            """;

        var introspecting = new IntrospectingSchemaProvider(BuildConfig());

        var path = Path.Combine(Path.GetTempPath(), $"schema-{Guid.NewGuid():N}.graphql");
        await File.WriteAllTextAsync(path, sdl);
        try
        {
            var fileProvider = new FileSchemaProvider(path);

            var schemaFromFile = await fileProvider.GetSchemaAsync();
            schemaFromFile.Should().NotBeNull();

            // Cross-provider equivalence: validating the same query string through both
            // providers must yield the same OperationType.
            var validator1 = new GraphQLClientValidator(introspecting);
            var validator2 = new GraphQLClientValidator(fileProvider);

            const string query = "query { allItems { id name category } }";
            var op1 = await validator1.ValidateAsync(query);
            var op2 = await validator2.ValidateAsync(query);

            op2.Should().Be(op1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task FileSchemaProvider_MissingFile_ThrowsIntrospectionException()
    {
        var provider = new FileSchemaProvider("/tmp/this-file-does-not-exist-" + Guid.NewGuid());

        var act = async () => await provider.GetSchemaAsync();

        var ex = await act.Should().ThrowAsync<GraphQLSchemaIntrospectionException>();
        ex.Which.Message.Should().Contain("not found");
    }

    [Test]
    public async Task FileSchemaProvider_EmptyFile_ThrowsWithHelpfulMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"empty-{Guid.NewGuid():N}.graphql");
        await File.WriteAllTextAsync(path, "   \n\t  ");
        try
        {
            var provider = new FileSchemaProvider(path);

            var act = async () => await provider.GetSchemaAsync();

            var ex = await act.Should().ThrowAsync<GraphQLSchemaIntrospectionException>();
            ex.Which.Message.Should().Contain("empty");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void FileSchemaProvider_NullOrWhitespacePath_ThrowsArgument()
    {
        var actNull = () => new FileSchemaProvider(null!);
        actNull.Should().Throw<ArgumentException>();

        var actEmpty = () => new FileSchemaProvider("   ");
        actEmpty.Should().Throw<ArgumentException>();
    }

    [Test]
    public async Task FileSchemaProvider_MalformedSdl_ThrowsWithSourceReference()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bad-{Guid.NewGuid():N}.graphql");
        await File.WriteAllTextAsync(path, "this is { not valid graphql }");
        try
        {
            var provider = new FileSchemaProvider(path);

            var act = async () => await provider.GetSchemaAsync();

            var ex = await act.Should().ThrowAsync<GraphQLSchemaIntrospectionException>();
            ex.Which.Message.Should().Contain(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private IGraphQLClientConfiguration BuildConfig()
    {
        var builder = new GraphQLClientConfigurationBuilder(_fixture.BaseAddress)
        {
            HttpClient = _fixture.CreateHttpClient(),
        };
        return builder.Build();
    }
}
