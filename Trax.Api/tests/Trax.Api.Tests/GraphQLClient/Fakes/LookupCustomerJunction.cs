using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.GraphQLClient.Fakes;

internal sealed class LookupCustomerJunction : Junction<LookupCustomerInput, LookupCustomerOutput>
{
    public override Task<LookupCustomerOutput> Run(LookupCustomerInput input) =>
        Task.FromResult(
            new LookupCustomerOutput
            {
                Id = "cust-" + input.Email.GetHashCode(StringComparison.Ordinal).ToString("x"),
                Email = input.Email,
                CreditLimit = 50_000,
            }
        );
}
