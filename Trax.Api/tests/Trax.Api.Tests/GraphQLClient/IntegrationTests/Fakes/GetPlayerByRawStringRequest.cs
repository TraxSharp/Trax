using System.Text.Json.Serialization;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

public sealed class GetPlayerByRawStringRequest : IGraphQLClientRequest<PlayerProfile>
{
    public required string Id { get; init; }

    public string Query =>
        """
            query GetPlayer($id: String!) {
              player(id: $id) {
                id
                name
                level
                rank
                guild { id name }
                inventory { id name category }
              }
            }
            """;

    public object Variables => new { id = Id };
}
