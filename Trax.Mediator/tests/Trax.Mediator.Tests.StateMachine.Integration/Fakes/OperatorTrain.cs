using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>Needs the <c>Operator</c> role.</summary>
[TraxAuthorize(Roles = "Operator")]
public class OperatorTrain : ServiceTrain<OperatorInput, JobOutput>, IOperatorTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Chain<OperatorStep>().Resolve();
}
