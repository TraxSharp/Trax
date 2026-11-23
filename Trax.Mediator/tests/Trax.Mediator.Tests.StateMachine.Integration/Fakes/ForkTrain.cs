using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>A Parallel step whose second branch runs a plain junction.</summary>
public class ForkTrain : ServiceTrain<ForkInput, JobOutput>, IForkTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Parallel(p =>
                p.Branch("effect", b => b.Chain<ForkLeft>())
                    .Branch("plain", b => b.Chain<PlainForkRight>())
            )
            .Chain<ForkJoin>()
            .Resolve();
}
