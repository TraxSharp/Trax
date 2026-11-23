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
