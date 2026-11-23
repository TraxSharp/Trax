using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

// Deliberately not tagged - must be excluded from discovery.
public sealed class UntaggedQuery : IGraphQLClientRequest<PlayerProfile>
{
    public string Query => "query Untagged { player(id: \"x\") { id } }";
}
