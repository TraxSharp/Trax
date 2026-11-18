using HotChocolate;
using HotChocolate.Types;

namespace Trax.Api.Tests.GraphQLClient.Fixtures;

public class PlayersQueries
{
    public Player? TypedPlayerByRank(Rank rank, [Service] TestPlayerStore store) =>
        store.All().FirstOrDefault(p => p.Rank == rank);
}
