using AwesomeAssertions;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

[TraxOutboundQuery("Throws")]
internal sealed class ThrowingQuery : IGraphQLClientRequest<object>
{
    public string Query => throw new InvalidOperationException("query getter explodes");
}
