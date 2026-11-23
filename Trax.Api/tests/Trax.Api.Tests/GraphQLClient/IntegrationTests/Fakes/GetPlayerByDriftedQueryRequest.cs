using System.Text.Json.Serialization;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

/// <summary>
/// Targets the same field as <see cref="GetPlayerByRawStringRequest"/> but adds <c>nope</c>
/// — a field that does not exist on the schema. Used to assert the validator catches drift
/// before any HTTP call is made.
/// </summary>
public sealed class GetPlayerByDriftedQueryRequest : IGraphQLClientRequest<PlayerProfile>
{
    public required string Id { get; init; }

    public string Query =>
        """
            query GetPlayerDrifted($id: String!) {
              player(id: $id) { id name nope }
            }
            """;

    public object Variables => new { id = Id };
}
