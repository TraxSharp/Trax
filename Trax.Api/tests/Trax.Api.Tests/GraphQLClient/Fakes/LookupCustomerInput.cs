using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.GraphQLClient.Fakes;

// Trains decorated with [TraxQuery(Namespace = ...)] / [TraxMutation(Namespace = ...)]
// that get picked up by AddMediator's assembly scan. The TraxServerFixture spins up a
// real Trax GraphQL server backed by these, so end-to-end tests can prove the typed
// client's Path attribute pairs correctly with the envelope shape Trax itself emits.
//
// Namespace = "netsuiteClient" is deliberately specific so these don't collide with
// trains other test fixtures register against the same assembly scan.

public record LookupCustomerInput : IManifestProperties
{
    public required string Email { get; init; }
}
