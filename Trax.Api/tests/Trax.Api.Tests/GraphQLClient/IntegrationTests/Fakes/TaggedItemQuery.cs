using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

[TraxOutboundQuery("PlayerService")]
public sealed class TaggedItemQuery : IGraphQLClientRequest<IReadOnlyList<ItemSummary>>
{
    public string Query => "query AllItemsTagged { allItems { id } }";
}
