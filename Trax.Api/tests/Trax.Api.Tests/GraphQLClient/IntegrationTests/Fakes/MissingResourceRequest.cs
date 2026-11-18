using Trax.Api.GraphQL.Client;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

/// <summary>
/// Decorated with an attribute that points at a missing resource. Used to assert the loader
/// throws a clear "resource not found" exception rather than a stream-null NullReferenceException.
/// </summary>
[GraphQLQueryResource("NonExistent.graphql")]
public sealed class MissingResourceRequest : GraphQLResourceRequest<PlayerProfile>
{
    public override object Variables => new { };
}
