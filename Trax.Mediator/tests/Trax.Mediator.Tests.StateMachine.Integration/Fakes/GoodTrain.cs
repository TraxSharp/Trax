using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>A train a machine may invoke: a ServiceTrain built only from effect junctions.</summary>
public class GoodTrain : ServiceTrain<GoodInput, JobOutput>, IGoodTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Chain<GoodStep>().Resolve();
}
