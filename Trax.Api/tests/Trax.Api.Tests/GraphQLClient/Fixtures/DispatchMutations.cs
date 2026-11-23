using HotChocolate;
using HotChocolate.Types;

namespace Trax.Api.Tests.GraphQLClient.Fixtures;

public class DispatchMutations
{
    public NetsuiteMutations Netsuite() => new();
}
