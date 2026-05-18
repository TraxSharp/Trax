using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.Tests.GraphQLClient.Fixtures;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// Edge paths in the typed-query generator that the main matrix doesn't cover. Each test
/// catches a specific regression in the property->selection walk: misnamed fields,
/// not-skipped JsonIgnore, operation-name overrides not honored.
/// </summary>
[TestFixture]
public class TypedQueryGeneratorEdgePathsTests
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
    public void GraphQLField_OverridesPropertyNameInQuery()
    {
        var request = new FieldRenamedRequest { Id = "x" };

        var query = request.Query;

        // The C# property is DisplayName but the query must select `name` because of
        // [GraphQLField("name")]. Without this test, a regression that ignored the attribute
        // would produce a query selecting `displayName` which the server doesn't have, and
        // the failure would be a server-side validation error rather than a clear local
        // generation bug.
        query.Should().Contain("name");
        query.Should().NotContain("displayName");
    }

    [Test]
    public void JsonIgnore_OmitsPropertyFromSelection()
    {
        var request = new FieldRenamedRequest { Id = "x" };

        var query = request.Query;

        // InternalNote is [JsonIgnore]'d - it must not appear in the selection set.
        // A regression here would request a field the server might not have, breaking
        // validation, or worse, requesting one it does have and shipping unexpected data.
        query.Should().NotContain("internalNote");
        query.Should().NotContain("InternalNote");
    }

    [Test]
    public void GraphQLOperation_NameOverride_IsUsedInOperationLine()
    {
        var request = new FieldRenamedRequest { Id = "x" };

        var query = request.Query;

        // [GraphQLOperation(Name = "GetPlayerCustomOp")] overrides the default "FieldRenamed"
        // derived from the type name.
        query.Should().Contain("query GetPlayerCustomOp");
        query.Should().NotContain("query FieldRenamed");
    }

    [Test]
    public async Task GraphQLField_ExecutesAgainstServerSuccessfully()
    {
        // End-to-end: the renamed-field request actually works against a real server.
        // Pin down that the generator's output is server-compatible, not just textually correct.
        var result = await _executor.Run(new FieldRenamedRequest { Id = "player-1" });

        result.Id.Should().Be("player-1");
        result.DisplayName.Should().Be("Aragorn");
        result.Level.Should().Be(42);
        result.InternalNote.Should().BeNull("the field was JsonIgnored and never requested");
    }
}
