using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

/// <summary>
/// Mode E requests: query lives in an embedded <c>.graphql</c> resource declared by
/// <see cref="GraphQLQueryResourceAttribute"/>. The POCO is the same shape as the raw-string
/// counterpart so the matrix tests can assert byte-equal results across both modes.
/// </summary>
[GraphQLQueryResource("GetPlayerByResource.graphql")]
public sealed class GetPlayerByResourceRequest : GraphQLResourceRequest<PlayerProfile>
{
    public required string Id { get; init; }

    public override object Variables => new { id = Id };
}
