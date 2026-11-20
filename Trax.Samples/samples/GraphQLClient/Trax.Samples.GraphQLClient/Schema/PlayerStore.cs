using HotChocolate.Execution.Configuration;

namespace Trax.Samples.GraphQLClient.Schema;

public sealed class PlayerStore
{
    private readonly Dictionary<string, Player> _players;

    public PlayerStore()
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
        };
    }

    public Player? Get(string id) => _players.TryGetValue(id, out var p) ? p : null;

    public void Put(Player player) => _players[player.Id] = player;
}
