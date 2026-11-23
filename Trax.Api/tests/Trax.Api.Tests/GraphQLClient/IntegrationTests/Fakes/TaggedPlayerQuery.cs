using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

/// <summary>
/// Tagged outbound requests used by OutboundQueryDiscoveryTests. Two endpoints, two requests
/// each, plus a deliberately un-tagged request so the filter logic is exercised both ways.
/// </summary>
[TraxOutboundQuery("PlayerService")]
public sealed class TaggedPlayerQuery : IGraphQLClientRequest<PlayerProfile>
{
    public string Query => "query GetPlayerTagged($id: String!) { player(id: $id) { id } }";
}
