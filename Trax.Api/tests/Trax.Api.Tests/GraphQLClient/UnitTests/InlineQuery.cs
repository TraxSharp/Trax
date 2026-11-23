using AwesomeAssertions;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

[TraxOutboundQuery("Inline")]
internal sealed class InlineQuery : IGraphQLClientRequest<object>
{
    public string Query => "query    {  __typename }";
}
