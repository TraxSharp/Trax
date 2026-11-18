using System.Text.Json.Nodes;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.TypedSelectionProbes;

[GraphQLOperation(OperationType.Query, RootField = "assets")]
public sealed class AssetsByURLPathRequest : TypedRequest<IReadOnlyList<AssetView>>
{
    [GraphQLArgument("String!")]
    public required string URLPath { get; init; }
}
