using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class GoodStep : EffectJunction<GoodInput, JobOutput>
{
    public override Task<JobOutput> Run(GoodInput input) =>
        Task.FromResult(new JobOutput { Artifact = "artifact:" + input.Source });
}
