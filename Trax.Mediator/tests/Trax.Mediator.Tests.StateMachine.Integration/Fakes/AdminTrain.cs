using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>Needs a role, stricter than the state-machine mutations' authenticated user.</summary>
[TraxAuthorize(Roles = "admin")]
public class AdminTrain : ServiceTrain<AdminInput, JobOutput>, IAdminTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Chain<AdminStep>().Resolve();
}
