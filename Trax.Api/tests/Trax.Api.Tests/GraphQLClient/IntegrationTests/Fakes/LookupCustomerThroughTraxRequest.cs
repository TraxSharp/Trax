using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fakes;

// Distinct sub-namespace from the rest of the Fakes folder so the assembly-scan validator
// test (ValidatorBehaviorTests.ValidateAssembliesAsync_ScansAllRequestTypes) doesn't try
// to validate these requests against the flat TestSchema - they require the Trax server
// schema with discover.netsuiteClient that only TraxServerFixture exposes.
namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes.TraxServer;

[GraphQLOperation(
    OperationType.Query,
    Path = "discover.netsuiteClient",
    RootField = "lookupCustomer"
)]
public sealed class LookupCustomerThroughTraxRequest : TypedRequest<TypedLookupCustomerOutput>
{
    // Trax exposes its train inputs under the GraphQL field argument named "input".
    // HotChocolate's default naming convention strips a trailing "Input" suffix from the
    // CLR type name and appends "Input", so LookupCustomerInput -> LookupCustomerInput.
    [GraphQLArgument("LookupCustomerInput!", VariableName = "input")]
    public required LookupCustomerInput Input { get; init; }
}
