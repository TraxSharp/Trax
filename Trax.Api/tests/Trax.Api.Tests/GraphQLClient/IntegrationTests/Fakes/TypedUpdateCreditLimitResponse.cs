using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.Fakes;

// Distinct sub-namespace from the rest of the Fakes folder so the assembly-scan validator
// test (ValidatorBehaviorTests.ValidateAssembliesAsync_ScansAllRequestTypes) doesn't try
// to validate these requests against the flat TestSchema - they require the Trax server
// schema with discover.netsuiteClient that only TraxServerFixture exposes.
namespace Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes.TraxServer;

// The mutation response wrapper Trax generates per train: externalId is non-null;
// metadataId / workQueueId / output are nullable depending on execution mode.
[GraphQLType("UpdateCreditLimitResponse")]
public sealed record TypedUpdateCreditLimitResponse(
    string ExternalId,
    long? MetadataId,
    TypedUpdateCreditLimitOutput? Output
);
