using Trax.Api.GraphQL.Client;

namespace Trax.Samples.GraphQLClient.Requests.A_RawString;

/// <summary>
/// Mode A: the query lives as a raw string inline in C#. Use this when the query has
/// fragments, unions, or anything else mode D doesn't support.
/// </summary>
public sealed class GetPlayerByRawStringRequest : IGraphQLClientRequest<PlayerProfile>
{
    public required string Id { get; init; }

    public string Query =>
        """
            query GetPlayerRawString($id: String!) {
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
