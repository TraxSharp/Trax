using Trax.Api.GraphQL.Client.Typed;
using GraphQLType = Trax.Api.GraphQL.Client.Typed.GraphQLTypeAttribute;

namespace Trax.Samples.GraphQLClient.Requests.D_Typed;

/// <summary>
/// Mode D: the POCO IS the selection set. No query string anywhere; the library generates
/// it at startup from the result POCO's shape and validates it against the schema. Refactor
/// a property, the query updates with it.
/// </summary>
[GraphQLType("Player")]
public sealed record TypedPlayerProfile(
    string Id,
    string Name,
    int? Level,
    string Rank,
    TypedGuildSummary? Guild,
    IReadOnlyList<TypedItemSummary> Inventory
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
