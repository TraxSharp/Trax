using System.Text.Json.Nodes;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.TypedSelectionProbes;

[GraphQLOperation(OperationType.Query, RootField = "asset")]
public sealed class AssetRequest : TypedRequest<AssetView>
{
    [GraphQLArgument("ID!")]
    public required string Id { get; init; }
}
