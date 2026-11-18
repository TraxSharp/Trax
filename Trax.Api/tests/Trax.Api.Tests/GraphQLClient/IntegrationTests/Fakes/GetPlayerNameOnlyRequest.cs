using System.Text.Json.Serialization;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

public sealed class GetPlayerNameOnlyRequest : IGraphQLClientRequest<PlayerNameOnly>
{
    public required string Id { get; init; }

    public string Query =>
        """
            query GetPlayerNameOnly($id: String!) {
              player(id: $id) { id name level }
            }
            """;

    public object Variables => new { id = Id };
}
