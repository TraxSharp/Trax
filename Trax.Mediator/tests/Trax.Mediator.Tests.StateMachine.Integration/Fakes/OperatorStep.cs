using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class OperatorStep : EffectJunction<OperatorInput, JobOutput>
{
    public override Task<JobOutput> Run(OperatorInput input) => Task.FromResult(new JobOutput());
}
