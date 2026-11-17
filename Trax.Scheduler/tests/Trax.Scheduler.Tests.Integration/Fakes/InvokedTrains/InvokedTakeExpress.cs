using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

public sealed class InvokedTakeExpress : EffectJunction<InvokedStepInput, InvokedLaneTaken>
{
    public override Task<InvokedLaneTaken> Run(InvokedStepInput input) =>
        throw new InvalidOperationException("the express lane is closed");
}
