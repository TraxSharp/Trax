using HotChocolate;
using HotChocolate.Types;

namespace Trax.Api.Tests.GraphQLClient.Fixtures;

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
