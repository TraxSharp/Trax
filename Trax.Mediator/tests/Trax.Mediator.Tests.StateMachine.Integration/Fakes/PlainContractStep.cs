using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class PlainContractStep : Junction<ContractInput, JobOutput>, IPlainContract
{
    public override Task<JobOutput> Run(ContractInput input) => Task.FromResult(new JobOutput());
}
