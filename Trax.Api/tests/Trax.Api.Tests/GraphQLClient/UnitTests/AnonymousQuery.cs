using AwesomeAssertions;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

[TraxOutboundQuery("Anon")]
internal sealed class AnonymousQuery : IGraphQLClientRequest<object>
{
    public string Query => "query { __typename }";
}
