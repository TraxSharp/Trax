using System.Text.Json.Serialization;
using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

/// <summary>
/// Raw-string request with a nullable response type, used to exercise the default
/// <see cref="IGraphQLClientRequest{T}"/>.Extract's JSON-null short-circuit. Queries the
/// schema's nullable <c>player</c> field with an id that misses, so the server returns
/// <c>{ "player": null }</c> and the default extractor must return <see langword="null"/>
/// rather than throwing.
/// </summary>
public sealed class GetPlayerOrNullRequest : IGraphQLClientRequest<PlayerProfile?>
{
    public required string Id { get; init; }

    public string Query =>
        """
            query GetPlayerOrNull($id: String!) {
              player(id: $id) { id name level rank guild { id name } inventory { id name category } }
            }
            """;

    public object Variables => new { id = Id };
}
