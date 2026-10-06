using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fixtures;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// <c>[GraphQLField]</c> names the schema field; the response key is still what the JSON
/// deserializer reads for the property. When the two differ the generator aliases the field
/// (<c>displayName: name</c>), so the server answers under the key the property binds to and
/// no matching <c>[JsonPropertyName]</c> is needed.
/// </summary>
[TestFixture]
public class TypedQueryGeneratorFieldAliasTests
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
            .ConfigureHttpClient(_fixture.CreateHttpClient())
            .WithStrictness(ResponseStrictness.ThrowOnDrift);
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
    public void A_renamed_field_is_selected_under_the_propertys_response_key()
    {
        var query = new AliasedPlayerRequest { Id = "player-1" }.Query;

        query.Should().Contain("displayName: name");
    }

    [Test]
    public async Task A_renamed_field_is_populated_from_the_response()
    {
        var result = await _executor.Run(new AliasedPlayerRequest { Id = "player-1" });

        result.DisplayName.Should().Be("Aragorn");
        result.Level.Should().Be(42);
    }

    [Test]
    public void A_renamed_field_whose_json_name_is_not_a_graphql_name_is_refused()
    {
        var act = () => new BadAliasPlayerRequest { Id = "player-1" }.Query;

        act.Should().Throw<InvalidOperationException>().WithMessage("*DisplayName*display-name*");
    }
}

[GraphQLType("Player")]
public sealed record AliasedPlayer(
    string Id,
    [property: GraphQLField("name")] string DisplayName,
    int? Level
);

[GraphQLOperation(OperationType.Query, Name = "AliasedPlayer", RootField = "player")]
public sealed class AliasedPlayerRequest : TypedRequest<AliasedPlayer>
{
    [GraphQLArgument("String!", VariableName = "id")]
    public required string Id { get; init; }
}

[GraphQLType("Player")]
public sealed record BadAliasPlayer(
    string Id,
    [property:
        GraphQLField("name"),
        System.Text.Json.Serialization.JsonPropertyName("display-name")
    ]
        string DisplayName
);

[GraphQLOperation(OperationType.Query, Name = "BadAliasPlayer", RootField = "player")]
public sealed class BadAliasPlayerRequest : TypedRequest<BadAliasPlayer>
{
    [GraphQLArgument("String!", VariableName = "id")]
    public required string Id { get; init; }
}
