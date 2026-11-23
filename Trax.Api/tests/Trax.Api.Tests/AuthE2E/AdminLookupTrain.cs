using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

[TraxQuery(Namespace = "admin", Description = "Admin-only lookup.")]
[TraxAuthorize(Roles = "Admin")]
public class AdminLookupTrain : ServiceTrain<AdminLookupInput, AdminLookupOutput>, IAdminLookupTrain
{
    protected override Task<Either<Exception, AdminLookupOutput>> Junctions() =>
        Chain<AdminLookupJunction>().Resolve();
}
