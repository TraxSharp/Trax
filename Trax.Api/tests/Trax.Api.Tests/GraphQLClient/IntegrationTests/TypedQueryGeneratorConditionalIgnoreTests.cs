using System.Text.Json.Serialization;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fixtures;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// <c>[JsonIgnore(Condition = WhenWritingNull)]</c> and <c>WhenWritingDefault</c> only suppress a
/// property when <em>serializing</em> it. System.Text.Json still reads the property from a
/// response, so the typed generator has to select it; only the unconditional
/// <c>[JsonIgnore]</c> (Condition = Always) means "never on the wire".
/// </summary>
[TestFixture]
public class TypedQueryGeneratorConditionalIgnoreTests
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
    public void WhenWritingNull_property_is_still_selected()
    {
        var query = new ConditionallyIgnoredPlayerRequest { Id = "player-1" }.Query;

        query.Should().Contain("level");
    }

    [Test]
    public async Task WhenWritingNull_property_is_populated_from_the_response()
    {
        var result = await _executor.Run(new ConditionallyIgnoredPlayerRequest { Id = "player-1" });

        result.Name.Should().Be("Aragorn");
        result.Level.Should().Be(42, "the server has a level for player-1 and STJ reads it");
    }
}

[GraphQLType("Player")]
public sealed record ConditionallyIgnoredPlayer(
    string Id,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Level
);

[GraphQLOperation(OperationType.Query, Name = "ConditionallyIgnoredPlayer", RootField = "player")]
public sealed class ConditionallyIgnoredPlayerRequest : TypedRequest<ConditionallyIgnoredPlayer>
{
    [GraphQLArgument("String!", VariableName = "id")]
    public required string Id { get; init; }
}
