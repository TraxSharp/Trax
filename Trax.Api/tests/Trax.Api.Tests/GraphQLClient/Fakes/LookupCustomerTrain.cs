using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.GraphQLClient.Fakes;

[TraxAllowAnonymous]
[TraxQuery(Namespace = "netsuiteClient", Description = "Looks up a customer by email.")]
public class LookupCustomerTrain
    : ServiceTrain<LookupCustomerInput, LookupCustomerOutput>,
        ILookupCustomerTrain
{
    protected override Task<Either<Exception, LookupCustomerOutput>> Junctions() =>
        Chain<LookupCustomerJunction>().Resolve();
}
