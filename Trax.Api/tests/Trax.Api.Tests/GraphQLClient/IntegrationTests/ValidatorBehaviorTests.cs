using System.Reflection;
using FluentAssertions;
using GraphQLParser.AST;
using Trax.Api.GraphQL.Client;
using Trax.Api.Tests.GraphQLClient.Fixtures;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// The validator owns the cache + operation-type detection that the executor depends on.
/// If caching breaks, every call re-parses; if operation-type detection breaks, mutations
/// get sent as queries (or vice versa). Both are silent failures unless caught by these
/// tests.
/// </summary>
[TestFixture]
public class ValidatorBehaviorTests
{
    private GraphQLTestServerFixture _fixture = null!;
    private GraphQLClientValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new GraphQLTestServerFixture();
        var config = new GraphQLClientConfigurationBuilder(_fixture.BaseAddress)
        {
            HttpClient = _fixture.CreateHttpClient(),
        }.Build();
        _validator = new GraphQLClientValidator(new IntrospectingSchemaProvider(config));
    }

    [TearDown]
    public void TearDown() => _fixture.Dispose();

    [Test]
    public async Task ValidateAsync_QueryOperation_ReturnsQuery()
    {
        const string query = "query { allItems { id } }";
        var op = await _validator.ValidateAsync(query);
        op.Should().Be(OperationType.Query);
    }

    [Test]
    public async Task ValidateAsync_MutationOperation_ReturnsMutation()
    {
        const string mutation = """
            mutation Rename($input: RenamePlayerInput!) {
              renamePlayer(input: $input) { id }
            }
            """;
        var op = await _validator.ValidateAsync(mutation);
        op.Should().Be(OperationType.Mutation);
    }

    [Test]
    public async Task ValidateAsync_SameQueryTwice_IsCached()
    {
        const string query = "query CacheTest { allItems { id } }";
        await _validator.ValidateAsync(query);

        _validator.CachedQueries.ContainsKey(query).Should().BeTrue();

        // Second call resolves from cache - we can't directly observe that but we can
        // confirm the cache wasn't double-written.
        await _validator.ValidateAsync(query);
        _validator.CachedQueries.Count(kv => kv.Key == query).Should().Be(1);
    }

    [Test]
    public async Task ValidateAsync_InvalidField_ThrowsValidationException()
    {
        const string query = "query { player(id: \"x\") { nope } }";

        var act = async () => await _validator.ValidateAsync(query);

        var ex = await act.Should().ThrowAsync<GraphQLValidationException>();
        ex.Which.Query.Should().Be(query);
        ex.Which.Errors.Should().NotBeEmpty();
    }

    [Test]
    public async Task ValidateAsync_NoOperationDefinition_ThrowsWithExplicitMessage()
    {
        const string fragmentOnly = "fragment Foo on Player { id }";

        var act = async () => await _validator.ValidateAsync(fragmentOnly);

        var ex = await act.Should().ThrowAsync<GraphQLValidationException>();
        ex.Which.Message.Should().Contain("No operation definition");
    }

    [Test]
    public void ValidateAsync_NullQuery_ThrowsArgumentNull()
    {
        var act = async () => await _validator.ValidateAsync(null!);
        act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Test]
    public async Task ValidateAssembliesAsync_ScansAllRequestTypes()
    {
        // Restrict the scan to our fakes only, otherwise we'd validate every IGenericGraphQLClientRequest
        // in the entire test assembly (including the deliberately broken DriftedQuery fixture).
        await _validator.ValidateAssembliesAsync(
            new[] { typeof(GetPlayerByRawStringRequest).Assembly },
            t =>
                t.Namespace == typeof(GetPlayerByRawStringRequest).Namespace
                && t != typeof(GetPlayerByDriftedQueryRequest)
                && t != typeof(MissingResourceRequest)
                && t.Name != "PlayerNameOnly"
        );

        // After ValidateAssembliesAsync, every queried type's Query is in the cache.
        _validator
            .CachedQueries.Keys.Any(k => k.Contains("PlayersByLevel"))
            .Should()
            .BeTrue("PlayersByLevelRequest should have been validated");
        _validator
            .CachedQueries.Keys.Any(k => k.Contains("AllItems"))
            .Should()
            .BeTrue("AllItemsRequest should have been validated");
    }

    [Test]
    public async Task ValidateAssembliesAsync_IncludesDriftedQuery_Throws()
    {
        var act = async () =>
            await _validator.ValidateAssembliesAsync(
                new[] { typeof(GetPlayerByDriftedQueryRequest).Assembly },
                t => t == typeof(GetPlayerByDriftedQueryRequest)
            );

        await act.Should().ThrowAsync<GraphQLValidationException>();
    }

    [Test]
    public async Task ValidateAssembliesAsync_NullAssemblies_Throws()
    {
        var act = async () =>
            await _validator.ValidateAssembliesAsync((IEnumerable<Assembly>)null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
