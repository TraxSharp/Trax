using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.Tests.GraphQLClient.Fixtures;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// Mode D ships when the POCO becomes the source of truth: the library generates the query
/// at startup. These tests pin down (a) the generated query is shaped correctly (snapshot),
/// (b) it executes against the real server and returns the same data the hand-written
/// equivalents would, and (c) deleting/renaming a POCO property changes the generated query.
/// </summary>
[TestFixture]
public class TypedRequestTests
{
    private GraphQLTestServerFixture _fixture = null!;
    private ServiceProvider _services = null!;
    private IGraphQLClientExecutor _executor = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new GraphQLTestServerFixture();
        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(_fixture.BaseAddress)
            .ConfigureHttpClient(_fixture.CreateHttpClient());
        _services = services.BuildServiceProvider();
        _executor = _services.GetRequiredService<IGraphQLClientExecutor>();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        _fixture.Dispose();
    }

    [Test]
    public void GeneratedQuery_HasExpectedShape()
    {
        var request = new GetPlayerByTypedRequest { Id = "ignored" };

        var query = request.Query;

        query.Should().Contain("query GetPlayerByTyped");
        query.Should().Contain("$id: String!");
        query.Should().Contain("player(id: $id)");
        query.Should().Contain("id\n");
        query.Should().Contain("name\n");
        query.Should().Contain("level\n");
        query.Should().Contain("rank\n");
        query.Should().Contain("guild");
        query.Should().Contain("inventory");
        query.Should().Contain("category");
    }

    [Test]
    public void GeneratedQuery_IsStableAcrossInstances()
    {
        var first = new GetPlayerByTypedRequest { Id = "a" };
        var second = new GetPlayerByTypedRequest { Id = "b" };

        ReferenceEquals(first.Query, second.Query).Should().BeTrue("generation is cached per type");
    }

    [Test]
    public void Variables_BoundFromAnnotatedProperties()
    {
        var request = new GetPlayerByTypedRequest { Id = "player-7" };

        var vars = request.Variables;

        vars.Should().BeOfType<Dictionary<string, object?>>();
        var dict = (Dictionary<string, object?>)vars!;
        dict.Should().ContainKey("id").WhoseValue.Should().Be("player-7");
    }

    [Test]
    public async Task TypedRequest_ExecutesAgainstRealServer()
    {
        var result = await _executor.Run(new GetPlayerByTypedRequest { Id = "player-1" });

        result.Should().NotBeNull();
        result.Id.Should().Be("player-1");
        result.Name.Should().Be("Aragorn");
        result.Level.Should().Be(42);
        result.Rank.Should().Be("GOLD");
        result.Guild.Should().NotBeNull();
        result.Guild!.Name.Should().Be("Dragonsworn");
        result.Inventory.Should().HaveCount(2);
    }

    [Test]
    public async Task TypedRequest_NoArguments_ExecutesAgainstRealServer()
    {
        var items = await _executor.Run(new AllItemsByTypedRequest());

        items.Should().HaveCount(3);
        items.Select(i => i.Category).Should().BeEquivalentTo(["WEAPON", "ARMOR", "CONSUMABLE"]);
    }

    [Test]
    public async Task CrossMode_TypedResultMatchesRawString()
    {
        // The whole point of having three modes is that they converge on the same answer.
        // If this fails, the typed generator is selecting different fields than the raw
        // query, or projecting them differently.
        var raw = await _executor.Run(new GetPlayerByRawStringRequest { Id = "player-1" });
        var typed = await _executor.Run(new GetPlayerByTypedRequest { Id = "player-1" });

        typed.Id.Should().Be(raw.Id);
        typed.Name.Should().Be(raw.Name);
        typed.Level.Should().Be(raw.Level);
        typed.Rank.Should().Be(raw.Rank);
        typed.Guild?.Id.Should().Be(raw.Guild?.Id);
        typed.Inventory.Select(i => i.Id).Should().BeEquivalentTo(raw.Inventory.Select(i => i.Id));
    }

    [Test]
    public void Generate_MissingGraphQLOperationAttribute_Throws()
    {
        var act = () =>
            Trax.Api.GraphQL.Client.Typed.TypedQueryGenerator.Generate(
                typeof(UnannotatedTypedRequest),
                typeof(TypedItemSummary)
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*GraphQLOperation*");
    }

    [Test]
    public void Generate_MissingGraphQLTypeOnResult_Throws()
    {
        var act = () =>
            Trax.Api.GraphQL.Client.Typed.TypedQueryGenerator.Generate(
                typeof(AllItemsByTypedRequest),
                typeof(PocoWithoutGraphQLType)
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*GraphQLType*");
    }

    // Helper types for the failure tests.
    public sealed class UnannotatedTypedRequest
        : Trax.Api.GraphQL.Client.Typed.TypedRequest<TypedItemSummary> { }

    public sealed record PocoWithoutGraphQLType(string Id, string Name);

    #region Path (nested envelope) end-to-end

    [Test]
    public async Task NestedPath_TypedRequest_ExecutesAgainstRealServer()
    {
        // The schema exposes discover.netsuite.typedCustomer; the request's Path declares
        // exactly that nesting. A regression in either the generator or the extractor
        // surfaces here as a request that the server can't validate or a response the
        // client can't deserialize.
        var result = await _executor.Run(new GetNestedCustomerByEmailRequest { Email = "Aragorn" });

        result.Should().NotBeNull();
        result!.Id.Should().Be("player-1");
        result.Name.Should().Be("Aragorn");
        result.Rank.Should().Be("GOLD");
    }

    [Test]
    public async Task NestedPath_NoArguments_ExecutesAgainstRealServer()
    {
        var result = await _executor.Run(new GetNestedCustomersRequest());

        result.Should().HaveCount(2);
        result.Select(i => i.Id).Should().BeEquivalentTo(["cust-1", "cust-2"]);
    }

    [Test]
    public async Task NestedPath_DifferentNamespace_ExecutesAgainstRealServer()
    {
        // Same client, different namespace under discover. Proves the generator doesn't
        // bake one specific namespace into the output and the executor handles each
        // request's Path independently.
        var result = await _executor.Run(new GetNestedPlayerByRankRequest { Rank = "GOLD" });

        result.Should().NotBeNull();
        result!.Rank.Should().Be("GOLD");
    }

    [Test]
    public async Task NestedPath_NullLeaf_ReturnsNull()
    {
        // The schema returns null when no customer matches. The extractor must propagate
        // null through the nested envelope rather than throwing.
        var result = await _executor.Run(
            new GetNestedCustomerByEmailRequest { Email = "no-such-name" }
        );

        result.Should().BeNull();
    }

    [Test]
    public void NestedPath_GeneratedQuery_IsCachedPerType()
    {
        // Per-type caching matters because the generator parses Path on every cache miss.
        // If caching keyed on something else (e.g., type name without namespace), two
        // requests sharing a name would clobber each other.
        var first = new GetNestedCustomerByEmailRequest { Email = "a" };
        var second = new GetNestedCustomerByEmailRequest { Email = "b" };

        ReferenceEquals(first.Query, second.Query).Should().BeTrue();
    }

    [Test]
    public void NestedPath_Mutation_GeneratorEmitsMutationKeyword()
    {
        // The generator branches on OperationType. A regression that hardcoded the keyword
        // to "query" would produce a query-shaped string for a mutation request, which the
        // server would either reject as an unknown root field or, worse, silently route to
        // the wrong root type.
        var request = new RenameNestedCustomerRequest
        {
            Input = new RenamePlayerInput("player-1", "x"),
        };

        var query = request.Query;

        query.Should().StartWith("mutation RenameNestedCustomer");
        query.Should().Contain("dispatch {");
        query.Should().Contain("netsuite {");
        query.Should().Contain("renameCustomer(input: $input)");
    }

    [Test]
    public async Task NestedPath_Mutation_ExecutesAgainstRealServer()
    {
        // Same end-to-end path as a nested query, but exercising the mutation branch of
        // every component: generator (mutation keyword), HotChocolate routing (RootMutation
        // vs RootQuery), and the executor's mutation HTTP path.
        var result = await _executor.Run(
            new RenameNestedCustomerRequest
            {
                Input = new RenamePlayerInput("player-2", "Bilbo Baggins"),
            }
        );

        result.Should().NotBeNull();
        result.Id.Should().Be("player-2");
        result.Name.Should().Be("Bilbo Baggins");
    }

    #endregion
}
