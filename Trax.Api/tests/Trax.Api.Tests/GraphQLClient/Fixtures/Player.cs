using HotChocolate;
using HotChocolate.Types;

namespace Trax.Api.Tests.GraphQLClient.Fixtures;

public record Player(
    string Id,
    string Name,
    int? Level,
    Rank Rank,
    Guild? Guild,
    IReadOnlyList<Item> Inventory
);
