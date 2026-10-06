using System.Text.Json.Nodes;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.TypedSelectionProbes;

[GraphQLType("Asset")]
public sealed class AssetView
{
    public string ID { get; init; } = "";

    public string URLPath { get; init; } = "";

    public Uri? Link { get; init; }

    public Guid Key { get; init; }

    public JsonObject? Metadata { get; init; }

    public Version? Version { get; init; }

    public AssetPart[] Parts { get; init; } = [];

    public string this[int index] => ID;
}

[GraphQLOperation(OperationType.Query, RootField = "asset")]
public sealed class AssetRequest : TypedRequest<AssetView>
{
    [GraphQLArgument("ID!")]
    public required string Id { get; init; }
}

[GraphQLOperation(OperationType.Query, RootField = "assets")]
public sealed class AssetsByURLPathRequest : TypedRequest<IReadOnlyList<AssetView>>
{
    [GraphQLArgument("String!")]
    public required string URLPath { get; init; }
}

/// <summary>A request shape shared by closed requests; never validated itself.</summary>
public class PagedAssetRequest<TView> : TypedRequest<IReadOnlyList<TView>>
{
    [GraphQLArgument("String!")]
    public string URLPath { get; init; } = "";
}

[GraphQLOperation(OperationType.Query, Name = "PagedAssets", RootField = "assets")]
public sealed class PagedAssetsRequest : PagedAssetRequest<AssetView>;

/// <summary>A nested result type with no <c>[GraphQLType]</c>: still an object type.</summary>
public sealed class AssetPart
{
    public string Name { get; init; } = "";
}

[GraphQLOperation(OperationType.Query, RootField = "assets")]
public sealed class AssetsByURLPathAndOwnerRequest : TypedRequest<IReadOnlyList<AssetView>>
{
    [GraphQLArgument("String!")]
    public required string URLPath { get; init; }

    [GraphQLArgument("ID")]
    public string? OwnerID { get; init; }
}
