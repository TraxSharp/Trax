using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

public sealed class InvokedLaneFinish : EffectJunction<InvokedLaneTaken, InvokedStepOutput>
{
    public override Task<InvokedStepOutput> Run(InvokedLaneTaken input) =>
        Task.FromResult(
            new InvokedStepOutput { Artifact = $"lane:{input.Lane}:{input.Note}", Accepted = true }
        );
}
