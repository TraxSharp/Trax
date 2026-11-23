using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>Names a class in IChain, which the chain recorder refuses.</summary>
public class RefusedChainTrain : ServiceTrain<RefusedInput, JobOutput>, IRefusedChainTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        IChain<RefusedStep>().Resolve();
}
