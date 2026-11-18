using System.Text.Json.Nodes;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.TypedSelectionProbes;

/// <summary>A request shape shared by closed requests; never validated itself.</summary>
public class PagedAssetRequest<TView> : TypedRequest<IReadOnlyList<TView>>
{
    [GraphQLArgument("String!")]
    public string URLPath { get; init; } = "";
}
