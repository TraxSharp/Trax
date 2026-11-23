using HotChocolate;
using HotChocolate.Types;

namespace Trax.Api.Tests.GraphQLClient.Fixtures;

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
