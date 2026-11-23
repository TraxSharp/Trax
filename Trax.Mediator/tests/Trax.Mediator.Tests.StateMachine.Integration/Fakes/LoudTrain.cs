using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>Broadcast: its subscribers see every run's output.</summary>
[TraxBroadcast]
public class LoudTrain : ServiceTrain<LoudInput, JobOutput>, ILoudTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Chain<LoudStep>().Resolve();
}
