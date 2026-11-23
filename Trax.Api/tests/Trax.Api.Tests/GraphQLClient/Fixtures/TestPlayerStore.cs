using HotChocolate;
using HotChocolate.Types;

namespace Trax.Api.Tests.GraphQLClient.Fixtures;

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

        _players = new Dictionary<string, Player>(StringComparer.Ordinal)
        {
            ["player-1"] = new Player("player-1", "Aragorn", 42, Rank.Gold, guild, inventory),
            ["player-2"] = new Player(
                "player-2",
                "Bilbo",
                Level: null,
                Rank.Bronze,
                Guild: null,
                Array.Empty<Item>()
            ),
            ["player-3"] = new Player("player-3", "Gandalf", 99, Rank.Gold, guild, inventory),
        };
    }

    public Player? Get(string id) => _players.TryGetValue(id, out var p) ? p : null;

    public IEnumerable<Player> All() => _players.Values;

    public void Put(Player player) => _players[player.Id] = player;
}
