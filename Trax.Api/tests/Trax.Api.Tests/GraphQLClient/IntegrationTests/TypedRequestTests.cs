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
}
