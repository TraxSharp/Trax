using AwesomeAssertions;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Trax;
using Trax.Api.GraphQL.Client.Typed;

namespace Trax.Api.Tests.GraphQLClient.UnitTests;

// Non-file scope: the `file` modifier mangles Type.Name with a per-file hash, which would
// break TypedQueryGenerator's name-based operation derivation (StripRequestSuffix). Other
// fakes in this file are file-scoped because they hard-code their Query strings and don't
// go through the generator.
[TraxOutboundQuery("Nested")]
[GraphQLOperation(OperationType.Query, Path = "discover.netsuite", RootField = "thing")]
internal sealed class NestedPathRequest : TypedRequest<NestedPathPayload>
{
    // No-op; the discovery only reads the Query string.
}
