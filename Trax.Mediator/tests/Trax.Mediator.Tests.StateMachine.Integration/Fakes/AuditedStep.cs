using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class AuditedStep : EffectJunction<AuditedInput, JobOutput>
{
    public override Task<JobOutput> Run(AuditedInput input) => Task.FromResult(new JobOutput());
}
