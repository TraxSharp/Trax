using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public class ContractTrain : ServiceTrain<ContractInput, JobOutput>, IContractTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        IChain<IPlainContract>().Resolve();
}
