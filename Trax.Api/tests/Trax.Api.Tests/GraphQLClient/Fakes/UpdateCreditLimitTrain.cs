using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.GraphQLClient.Fakes;

[TraxAllowAnonymous]
[TraxMutation(Namespace = "netsuiteClient", Description = "Updates a customer's credit limit.")]
public class UpdateCreditLimitTrain
    : ServiceTrain<UpdateCreditLimitInput, UpdateCreditLimitOutput>,
        IUpdateCreditLimitTrain
{
    protected override Task<Either<Exception, UpdateCreditLimitOutput>> Junctions() =>
        Chain<UpdateCreditLimitJunction>().Resolve();
}
