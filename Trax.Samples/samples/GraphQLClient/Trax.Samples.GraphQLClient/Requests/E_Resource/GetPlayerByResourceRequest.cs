using Trax.Api.GraphQL.Client;

namespace Trax.Samples.GraphQLClient.Requests.E_Resource;

/// <summary>
/// Mode E: query string lives in a sibling <c>.graphql</c> file as an embedded resource.
/// You get IDE syntax highlighting and the C# file shrinks to a POCO + attribute.
/// </summary>
[GraphQLQueryResource("GetPlayerByResource.graphql")]
public sealed class GetPlayerByResourceRequest : GraphQLResourceRequest<PlayerProfile>
{
    public required string Id { get; init; }

    public override object Variables => new { id = Id };
}
