using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fixtures;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

[GraphQLOperation(OperationType.Query, Path = "discover.players", RootField = "typedPlayerByRank")]
public sealed class GetNestedPlayerByRankRequest : TypedRequest<TypedPlayerProfile?>
{
    [GraphQLArgument("Rank!", VariableName = "rank")]
    public required string Rank { get; init; }
}
