using HotChocolate;
using HotChocolate.Types;

namespace Trax.Api.Tests.GraphQLClient.Fixtures;

public class DiscoverQueries
{
    public NetsuiteQueries Netsuite() => new();

    public PlayersQueries Players() => new();
}
