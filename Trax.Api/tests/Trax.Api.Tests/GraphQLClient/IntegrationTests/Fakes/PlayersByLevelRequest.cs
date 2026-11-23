using System.Text.Json.Serialization;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

public sealed class PlayersByLevelRequest : IGraphQLClientRequest<IReadOnlyList<PlayerProfile>>
{
    public required int Min { get; init; }

    public string Query =>
        """
            query PlayersByLevel($min: Int!) {
              playersByLevel(min: $min) {
                id
                name
                level
                rank
                guild { id name }
                inventory { id name category }
              }
            }
            """;

    public object Variables => new { min = Min };
}
