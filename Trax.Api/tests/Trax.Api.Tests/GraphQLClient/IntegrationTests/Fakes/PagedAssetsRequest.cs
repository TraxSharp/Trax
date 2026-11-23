using System.Text.Json.Nodes;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.TypedSelectionProbes;

[GraphQLOperation(OperationType.Query, Name = "PagedAssets", RootField = "assets")]
public sealed class PagedAssetsRequest : PagedAssetRequest<AssetView>;
