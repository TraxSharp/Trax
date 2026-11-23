using System.Text.Json.Serialization;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

public sealed class RenamePlayerRequest : IGraphQLClientRequest<PlayerProfile>
{
    public required string Id { get; init; }
    public required string NewName { get; init; }

    public string Query =>
        """
            mutation Rename($input: RenamePlayerInput!) {
              renamePlayer(input: $input) {
                id
                name
                level
                rank
                guild { id name }
                inventory { id name category }
              }
            }
            """;

    public object Variables => new { input = new { id = Id, newName = NewName } };
}
