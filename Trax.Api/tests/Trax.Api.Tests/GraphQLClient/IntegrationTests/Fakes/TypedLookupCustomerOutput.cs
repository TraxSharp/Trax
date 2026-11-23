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
