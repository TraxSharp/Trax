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

[GraphQLType("Guild")]
public sealed record TypedGuildSummary(string Id, string Name);

[GraphQLType("Item")]
public sealed record TypedItemSummary(string Id, string Name, string Category);

[GraphQLOperation(OperationType.Query, RootField = "player")]
public sealed class GetPlayerByTypedRequest : TypedRequest<TypedPlayerProfile>
{
    [GraphQLArgument("String!", VariableName = "id")]
    public required string Id { get; init; }
}

[GraphQLOperation(OperationType.Query, RootField = "allItems")]
public sealed class AllItemsByTypedRequest : TypedRequest<IReadOnlyList<TypedItemSummary>>
{
    // No arguments; AllItems takes none.
}
