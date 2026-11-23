using HotChocolate.Execution.Configuration;

namespace Trax.Samples.GraphQLClient.Schema;

public record Player(
    string Id,
    string Name,
    int? Level,
    Rank Rank,
    Guild? Guild,
    IReadOnlyList<Item> Inventory
);
