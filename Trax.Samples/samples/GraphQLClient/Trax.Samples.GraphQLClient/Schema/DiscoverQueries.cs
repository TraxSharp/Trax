using HotChocolate.Execution.Configuration;

namespace Trax.Samples.GraphQLClient.Schema;

public class DiscoverQueries
{
    public PlayersQueries Players() => new();
}
