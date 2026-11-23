using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

/// <summary>
/// Mode D fakes. The POCO is the source of truth; the library generates the query at
/// startup. The result types deliberately mirror the Mode A / Mode E POCOs so the matrix
/// can assert byte-equal results across all three modes.
/// </summary>
[GraphQLType("Player")]
public sealed record TypedPlayerProfile(
    string Id,
    string Name,
    int? Level,
    string Rank,
    [property: GraphQLField("guild")] TypedGuildSummary? Guild,
    [property: GraphQLField("inventory")] IReadOnlyList<TypedItemSummary> Inventory
);
