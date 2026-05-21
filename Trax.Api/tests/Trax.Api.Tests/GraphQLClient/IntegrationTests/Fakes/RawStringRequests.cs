using System.Text.Json.Serialization;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

/// <summary>
/// Mode A requests: query is a raw inline string. Response POCO is hand-written. These
/// exercise the simplest path through the executor: ValidateAsync, default Extract unwrap,
/// deserialize.
/// </summary>
public record PlayerProfile(
    string Id,
    string Name,
    int? Level,
    string Rank,
    GuildSummary? Guild,
    IReadOnlyList<ItemSummary> Inventory
);

public record GuildSummary(string Id, string Name);

public record ItemSummary(string Id, string Name, string Category);

public sealed class GetPlayerByRawStringRequest : IGraphQLClientRequest<PlayerProfile>
{
    public required string Id { get; init; }

    public string Query =>
        """
            query GetPlayer($id: String!) {
              player(id: $id) {
                id
                name
                level
                rank
                guild { id name }
                inventory { id name category }
              }
            }
            """;

    public object Variables => new { id = Id };
}

public sealed class PlayersByLevelRequest : IGraphQLClientRequest<IReadOnlyList<PlayerProfile>>
{
    public required int Min { get; init; }

    public string Query =>
        """
            query PlayersByLevel($min: Int!) {
              playersByLevel(min: $min) {
                id
                name
                level
                rank
                guild { id name }
                inventory { id name category }
              }
            }
            """;

    public object Variables => new { min = Min };
}

public sealed class AllItemsRequest : IGraphQLClientRequest<IReadOnlyList<ItemSummary>>
{
    public string Query =>
        """
            query AllItems {
              allItems { id name category }
            }
            """;
}

public sealed class RenamePlayerRequest : IGraphQLClientRequest<PlayerProfile>
{
    public required string Id { get; init; }
    public required string NewName { get; init; }

    public string Query =>
        """
            mutation Rename($input: RenamePlayerInput!) {
              renamePlayer(input: $input) {
                id
                name
                level
                rank
                guild { id name }
                inventory { id name category }
              }
            }
            """;

    public object Variables => new { input = new { id = Id, newName = NewName } };
}

/// <summary>
/// Targets the same field as <see cref="GetPlayerByRawStringRequest"/> but adds <c>nope</c>
/// — a field that does not exist on the schema. Used to assert the validator catches drift
/// before any HTTP call is made.
/// </summary>
public sealed class GetPlayerByDriftedQueryRequest : IGraphQLClientRequest<PlayerProfile>
{
    public required string Id { get; init; }

    public string Query =>
        """
            query GetPlayerDrifted($id: String!) {
              player(id: $id) { id name nope }
            }
            """;

    public object Variables => new { id = Id };
}

/// <summary>
/// Strict-extract drift fixture. The query selects <c>id</c>, <c>name</c>, <c>level</c> but
/// the POCO omits <c>level</c>. With strictness on, this should throw or warn.
/// </summary>
public record PlayerNameOnly([property: JsonPropertyName("id")] string Id, string Name);

public sealed class GetPlayerNameOnlyRequest : IGraphQLClientRequest<PlayerNameOnly>
{
    public required string Id { get; init; }

    public string Query =>
        """
            query GetPlayerNameOnly($id: String!) {
              player(id: $id) { id name level }
            }
            """;

    public object Variables => new { id = Id };
}

/// <summary>
/// Raw-string request with a nullable response type, used to exercise the default
/// <see cref="IGraphQLClientRequest{T}"/>.Extract's JSON-null short-circuit. Queries the
/// schema's nullable <c>player</c> field with an id that misses, so the server returns
/// <c>{ "player": null }</c> and the default extractor must return <c>null</c>
/// rather than throwing.
/// </summary>
public sealed class GetPlayerOrNullRequest : IGraphQLClientRequest<PlayerProfile?>
{
    public required string Id { get; init; }

    public string Query =>
        """
            query GetPlayerOrNull($id: String!) {
              player(id: $id) { id name level rank guild { id name } inventory { id name category } }
            }
            """;

    public object Variables => new { id = Id };
}

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

public sealed class TwoRootsRequest : IGraphQLClientRequest<TwoRoots>
{
    public required string Id { get; init; }

    public string Query =>
        """
            query TwoRoots($id: String!) {
              allItems { id name category }
              player(id: $id) { id name level rank guild { id name } inventory { id name category } }
            }
            """;

    public object Variables => new { id = Id };
}
