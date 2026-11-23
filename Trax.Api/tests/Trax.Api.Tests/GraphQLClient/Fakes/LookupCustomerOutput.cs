using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.GraphQLClient.Fakes;

public record LookupCustomerOutput
{
    public required string Id { get; init; }
    public required string Email { get; init; }
    public required int CreditLimit { get; init; }
}
