using HotChocolate.Execution.Configuration;

namespace Trax.Samples.GraphQLClient.Schema;

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
