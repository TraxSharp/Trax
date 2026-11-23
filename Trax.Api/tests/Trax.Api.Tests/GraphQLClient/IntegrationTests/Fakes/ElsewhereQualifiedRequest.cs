using Trax.Api.GraphQL.Client;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

// A namespace that matches no folder, so the loader cannot find these requests' resources by the
// conventional {namespace}.{name} lookup.
namespace Trax.Api.Tests.GraphQLClient.ResourceResolutionProbes;

[GraphQLQueryResource("B/Shared.graphql")]
public sealed class ElsewhereQualifiedRequest : GraphQLResourceRequest<PlayerProfile>
{
    public override object Variables => new { };
}
