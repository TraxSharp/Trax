using System.Text.Json.Serialization;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

/// <summary>
/// Response shape for a multi-root-field query: two sibling roots under <c>data</c>.
/// The default <see cref="IGraphQLClientRequest{T}.UnwrapDataElement"/> must NOT unwrap
/// when more than one top-level property exists — it has to hand the whole envelope to
/// the deserializer so both fields land on the POCO.
/// </summary>
public record TwoRoots(
    [property: JsonPropertyName("allItems")] IReadOnlyList<ItemSummary> AllItems,
    [property: JsonPropertyName("player")] PlayerProfile? Player
);
