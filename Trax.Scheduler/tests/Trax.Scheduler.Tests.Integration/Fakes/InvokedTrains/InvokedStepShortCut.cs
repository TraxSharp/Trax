using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

public sealed class InvokedStepShortCut : EffectJunction<InvokedStepGated, InvokedStepOutput>
{
    public override Task<InvokedStepOutput> Run(InvokedStepGated input) =>
        string.Equals(input.Mode, InvokedStepModes.Short, StringComparison.Ordinal)
            ? Task.FromResult(
                new InvokedStepOutput { Artifact = "short:" + input.Note, Accepted = true }
            )
            : throw new InvalidOperationException("no short cut");
}
