using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

[GraphQLQueryResource("RenamePlayerByResource.graphql")]
public sealed class RenamePlayerByResourceRequest : GraphQLResourceRequest<PlayerProfile>
{
    public required string Id { get; init; }
    public required string NewName { get; init; }

    public override object Variables => new { input = new { id = Id, newName = NewName } };
}
