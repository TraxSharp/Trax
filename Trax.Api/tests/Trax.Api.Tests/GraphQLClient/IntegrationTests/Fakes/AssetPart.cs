using System.Text.Json.Nodes;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.TypedSelectionProbes;

/// <summary>A nested result type with no <c>[GraphQLType]</c>: still an object type.</summary>
public sealed class AssetPart
{
    public string Name { get; init; } = "";
}
