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

[TraxOutboundQuery("PlayerService")]
public sealed class TaggedItemQuery : IGraphQLClientRequest<IReadOnlyList<ItemSummary>>
{
    public string Query => "query AllItemsTagged { allItems { id } }";
}

[TraxOutboundQuery("BillingService")]
public sealed class TaggedBillingMutation : IGraphQLClientRequest<PlayerProfile>
{
    public string Query =>
        "mutation BillUserTagged($input: RenamePlayerInput!) { renamePlayer(input: $input) { id } }";
}

// Deliberately not tagged - must be excluded from discovery.
public sealed class UntaggedQuery : IGraphQLClientRequest<PlayerProfile>
{
    public string Query => "query Untagged { player(id: \"x\") { id } }";
}
