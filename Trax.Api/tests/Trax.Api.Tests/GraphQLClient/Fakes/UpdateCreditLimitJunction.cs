using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.GraphQLClient.Fakes;

internal sealed class UpdateCreditLimitJunction
    : Junction<UpdateCreditLimitInput, UpdateCreditLimitOutput>
{
    public override Task<UpdateCreditLimitOutput> Run(UpdateCreditLimitInput input) =>
        Task.FromResult(
            new UpdateCreditLimitOutput
            {
                CustomerId = input.CustomerId,
                OldLimit = 50_000,
                NewLimit = input.NewLimit,
            }
        );
}
