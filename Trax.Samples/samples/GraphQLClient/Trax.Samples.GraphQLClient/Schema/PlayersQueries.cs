using HotChocolate.Execution.Configuration;

namespace Trax.Samples.GraphQLClient.Schema;

public class PlayersQueries
{
    public Player? LookupPlayer(string id, [Service] PlayerStore store) => store.Get(id);
}
