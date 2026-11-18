using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

[TraxMutation(Namespace = "admin", Description = "Admin-only wipe.")]
[TraxAuthorize(Policy = "AdminPolicy")]
public class WipeTrain : ServiceTrain<WipeInput, WipeOutput>, IWipeTrain
{
    protected override Task<Either<Exception, WipeOutput>> Junctions() =>
        Chain<WipeJunction>().Resolve();
}
