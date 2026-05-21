using HotChocolate;
using HotChocolate.Types;

namespace Trax.Api.Tests.GraphQLClient.Fixtures;

/// <summary>
/// A deliberately small HotChocolate schema exercised by every GraphQL client integration
/// test. Each shape (scalar, list, nullable, enum, input object, mutation) is included so
/// the test matrix can cover the full surface without needing a sprawling fake domain.
/// </summary>
public enum Rank
{
    Bronze,
    Silver,
    Gold,
}

public enum ItemCategory
{
    Weapon,
    Armor,
    Consumable,
}

public record Guild(string Id, string Name);

public record Item(string Id, string Name, ItemCategory Category);

public record Player(
    string Id,
    string Name,
    int? Level,
    Rank Rank,
    Guild? Guild,
    IReadOnlyList<Item> Inventory
);

public record RenamePlayerInput(string Id, string NewName);

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

public class DiscoverQueries
{
    public NetsuiteQueries Netsuite() => new();

    public PlayersQueries Players() => new();
}

public class NetsuiteQueries
{
    public Player? TypedCustomer(string email, [Service] TestPlayerStore store) =>
        store.All().FirstOrDefault(p => p.Name.Equals(email, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<Item> TypedCustomers() =>
        new[]
        {
            new Item("cust-1", "Acme", ItemCategory.Consumable),
            new Item("cust-2", "Beta", ItemCategory.Consumable),
        };
}

public class PlayersQueries
{
    public Player? TypedPlayerByRank(Rank rank, [Service] TestPlayerStore store) =>
        store.All().FirstOrDefault(p => p.Rank == rank);
}

public class TestMutation
{
    public Player RenamePlayer(RenamePlayerInput input, [Service] TestPlayerStore store)
    {
        var existing =
            store.Get(input.Id) ?? throw new GraphQLException($"Player '{input.Id}' not found.");
        var updated = existing with { Name = input.NewName };
        store.Put(updated);
        return updated;
    }

    // Mirrors the Trax server's dispatch.{namespace} envelope for mutations. Pairs with
    // the discover.{namespace} surface on TestQuery so the typed client can be exercised
    // against both halves of the Trax convention.
    public DispatchMutations Dispatch() => new();
}

public class DispatchMutations
{
    public NetsuiteMutations Netsuite() => new();
}

public class NetsuiteMutations
{
    public Player RenameCustomer(RenamePlayerInput input, [Service] TestPlayerStore store)
    {
        var existing =
            store.Get(input.Id) ?? throw new GraphQLException($"Player '{input.Id}' not found.");
        var updated = existing with { Name = input.NewName };
        store.Put(updated);
        return updated;
    }
}

/// <summary>
/// In-memory player store used by the test server. Seeded deterministically per fixture
/// instance so tests can assert on exact contents.
/// </summary>
public sealed class TestPlayerStore
{
    private readonly Dictionary<string, Player> _players;

    public TestPlayerStore()
    {
        var guild = new Guild("guild-1", "Dragonsworn");
        var inventory = new[]
        {
            new Item("item-1", "Sword", ItemCategory.Weapon),
            new Item("item-2", "Shield", ItemCategory.Armor),
        };

        _players = new Dictionary<string, Player>
        {
            ["player-1"] = new Player("player-1", "Aragorn", 42, Rank.Gold, guild, inventory),
            ["player-2"] = new Player(
                "player-2",
                "Bilbo",
                null,
                Rank.Bronze,
                null,
                Array.Empty<Item>()
            ),
            ["player-3"] = new Player("player-3", "Gandalf", 99, Rank.Gold, guild, inventory),
        };
    }

    public Player? Get(string id) => _players.TryGetValue(id, out var p) ? p : null;

    public IEnumerable<Player> All() => _players.Values;

    public void Put(Player player) => _players[player.Id] = player;
}
