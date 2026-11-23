using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fixtures;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

[GraphQLOperation(OperationType.Query, Path = "discover", RootField = "netsuite")]
public sealed class SingleLevelPathRequest : TypedRequest<NetsuiteNamespaceShape>
{
    // Single-segment path. The inner root field projects the namespace object itself.
}
