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
