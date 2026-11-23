using HotChocolate.Execution.Configuration;

namespace Trax.Samples.GraphQLClient.Schema;

public class PlayerQuery
{
    public Player? Player(string id, [Service] PlayerStore store) => store.Get(id);

    public IReadOnlyList<Item> AllItems() =>
        new[]
        {
            new Item("item-1", "Sword", ItemCategory.Weapon),
            new Item("item-2", "Shield", ItemCategory.Armor),
            new Item("item-3", "Potion", ItemCategory.Consumable),
        };

    // The discover.{namespace} envelope mirrors the shape Trax itself produces server-side
    // when trains are decorated with [TraxQuery(Namespace = "...")]. Clients query through
    // [GraphQLOperation(Path = "discover.players", RootField = "...")].
    public DiscoverQueries Discover() => new();
}
