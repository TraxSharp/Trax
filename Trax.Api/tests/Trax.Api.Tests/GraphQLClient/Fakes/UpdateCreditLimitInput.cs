using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.GraphQLClient.Fakes;

public record UpdateCreditLimitInput : IManifestProperties
{
    public required string CustomerId { get; init; }
    public required int NewLimit { get; init; }
}
