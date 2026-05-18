using HotChocolate.Execution.Configuration;

namespace Trax.Samples.GraphQLClient.Schema;

/// <summary>
/// Single source of truth for the sample server's GraphQL schema. The server's <c>Program.cs</c>
/// applies it via <c>AddGraphQLServer().ConfigureSchema(...)</c>, and the client uses the same
/// delegate via <c>AddTraxGraphQLClient(PlayerSchemaConfiguration.Configure)</c>. Because both
/// sides reference the same static method, schema drift between them is impossible by
/// construction.
/// </summary>
public static class PlayerSchemaConfiguration
{
    public static void Configure(IRequestExecutorBuilder builder) =>
        builder
            .AddQueryType<PlayerQuery>()
            .AddMutationType<PlayerMutation>()
            .DisableIntrospection(false);
}

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
        };
    }

    public Player? Get(string id) => _players.TryGetValue(id, out var p) ? p : null;

    public void Put(Player player) => _players[player.Id] = player;
}

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
}

public class PlayerMutation
{
    public Player RenamePlayer(RenamePlayerInput input, [Service] PlayerStore store)
    {
        var existing =
            store.Get(input.Id) ?? throw new GraphQLException($"Player '{input.Id}' not found.");
        var updated = existing with { Name = input.NewName };
        store.Put(updated);
        return updated;
    }
}
