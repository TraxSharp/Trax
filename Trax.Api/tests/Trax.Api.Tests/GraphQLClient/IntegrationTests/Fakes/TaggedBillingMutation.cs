using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

[TraxOutboundQuery("BillingService")]
public sealed class TaggedBillingMutation : IGraphQLClientRequest<PlayerProfile>
{
    public string Query =>
        "mutation BillUserTagged($input: RenamePlayerInput!) { renamePlayer(input: $input) { id } }";
}
