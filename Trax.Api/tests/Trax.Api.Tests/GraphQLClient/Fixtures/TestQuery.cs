using HotChocolate;
using HotChocolate.Types;

namespace Trax.Api.Tests.GraphQLClient.Fixtures;

public class TestQuery
{
    public Player? Player(string id, [Service] TestPlayerStore store) => store.Get(id);

    public IReadOnlyList<Player> PlayersByLevel(int min, [Service] TestPlayerStore store) =>
        store.All().Where(p => p.Level is { } lvl && lvl >= min).ToList();

    public IReadOnlyList<Item> AllItems() =>
        new[]
        {
            new Item("item-1", "Sword", ItemCategory.Weapon),
            new Item("item-2", "Shield", ItemCategory.Armor),
            new Item("item-3", "Potion", ItemCategory.Consumable),
        };

    // Mirrors the Trax server's discover.{namespace} envelope so the typed client can be
    // exercised against the exact nesting shape Trax itself produces. The non-discover
    // fields above keep the flat-path tests honest; the discover branch is new surface.
    public DiscoverQueries Discover() => new();
}
