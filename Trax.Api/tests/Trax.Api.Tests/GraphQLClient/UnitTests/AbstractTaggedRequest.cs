using AwesomeAssertions;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

[TraxOutboundQuery("AbstractBase")]
internal abstract class AbstractTaggedRequest : IGraphQLClientRequest<object>
{
    public abstract string Query { get; }
}
