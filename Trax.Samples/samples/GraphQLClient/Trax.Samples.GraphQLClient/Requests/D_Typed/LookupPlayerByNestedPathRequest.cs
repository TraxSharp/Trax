using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Samples.GraphQLClient.Requests.D_Typed;

/// <summary>
/// Mode D with a nested envelope. Path = "discover.players" makes the generator wrap the
/// root field in two layers of braces so the query targets
/// <c>query { discover { players { lookupPlayer(id: $id) { ... } } } }</c>. The default
/// extractor walks the same path before deserializing. Matches the shape Trax produces
/// server-side for <c>[TraxQuery(Namespace = "players")]</c>-decorated trains.
/// </summary>
[GraphQLOperation(OperationType.Query, Path = "discover.players", RootField = "lookupPlayer")]
public sealed class LookupPlayerByNestedPathRequest : TypedRequest<TypedPlayerProfile?>
{
    [GraphQLArgument("String!", VariableName = "id")]
    public required string Id { get; init; }
}
