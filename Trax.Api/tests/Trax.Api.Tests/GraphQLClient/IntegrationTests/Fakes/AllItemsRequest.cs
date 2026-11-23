using System.Text.Json.Serialization;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

public sealed class AllItemsRequest : IGraphQLClientRequest<IReadOnlyList<ItemSummary>>
{
    public string Query =>
        """
            query AllItems {
              allItems { id name category }
            }
            """;
}
