using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>Runs a plain junction, which never reads the run's cancel flag.</summary>
public class PlainJunctionTrain : ServiceTrain<PlainInput, JobOutput>, IPlainJunctionTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Chain<PlainStep>().Resolve();
}
