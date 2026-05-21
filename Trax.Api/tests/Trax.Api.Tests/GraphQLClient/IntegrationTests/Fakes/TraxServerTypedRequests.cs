using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fakes;

// Distinct sub-namespace from the rest of the Fakes folder so the assembly-scan validator
// test (ValidatorBehaviorTests.ValidateAssembliesAsync_ScansAllRequestTypes) doesn't try
// to validate these requests against the flat TestSchema - they require the Trax server
// schema with discover.netsuiteClient that only TraxServerFixture exposes.
namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes.TraxServer;

// Typed client requests targeting the Trax server's actual discover.{namespace} and
// dispatch.{namespace} envelopes (see TraxServerTrains.cs). Field names follow Trax's
// derivation rules: interface name minus 'I' minus 'Train' suffix, camelCased.
// e.g. ILookupCustomerTrain -> lookupCustomer.

[GraphQLType("LookupCustomerOutput")]
public sealed record TypedLookupCustomerOutput(string Id, string Email, int CreditLimit);

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

// The mutation response wrapper Trax generates per train: externalId is non-null;
// metadataId / workQueueId / output are nullable depending on execution mode.
[GraphQLType("UpdateCreditLimitResponse")]
public sealed record TypedUpdateCreditLimitResponse(
    string ExternalId,
    long? MetadataId,
    TypedUpdateCreditLimitOutput? Output
);

[GraphQLType("UpdateCreditLimitOutput")]
public sealed record TypedUpdateCreditLimitOutput(string CustomerId, int OldLimit, int NewLimit);

[GraphQLOperation(
    OperationType.Mutation,
    Path = "dispatch.netsuiteClient",
    RootField = "updateCreditLimit"
)]
public sealed class UpdateCreditLimitThroughTraxRequest
    : TypedRequest<TypedUpdateCreditLimitResponse>
{
    [GraphQLArgument("UpdateCreditLimitInput!", VariableName = "input")]
    public required UpdateCreditLimitInput Input { get; init; }
}
