using Trax.Api.GraphQL.Client;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

// A namespace that matches no folder, so the loader cannot find these requests' resources by the
// conventional {namespace}.{name} lookup.
namespace Trax.Api.Tests.GraphQLClient.ResourceResolutionProbes;

/// <summary>
/// Resource requests in a namespace that matches no folder, so the loader cannot use the
/// conventional <c>{namespace}.{name}</c> lookup.
/// </summary>
[GraphQLQueryResource("Player.graphql")]
public sealed class ElsewherePlayerRequest : GraphQLResourceRequest<PlayerProfile>
{
    public override object Variables => new { };
}

[GraphQLQueryResource("Shared.graphql")]
public sealed class ElsewhereSharedRequest : GraphQLResourceRequest<PlayerProfile>
{
    public override object Variables => new { };
}

[GraphQLQueryResource("B/Shared.graphql")]
public sealed class ElsewhereQualifiedRequest : GraphQLResourceRequest<PlayerProfile>
{
    public override object Variables => new { };
}
