using System.Text.Json.Nodes;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.TypedSelectionProbes;

[GraphQLOperation(OperationType.Query, RootField = "assets")]
public sealed class AssetsByURLPathAndOwnerRequest : TypedRequest<IReadOnlyList<AssetView>>
{
    [GraphQLArgument("String!")]
    public required string URLPath { get; init; }

    [GraphQLArgument("ID")]
    public string? OwnerID { get; init; }
}
