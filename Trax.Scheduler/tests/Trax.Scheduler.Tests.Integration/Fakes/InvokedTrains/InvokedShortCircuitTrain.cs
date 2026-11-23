using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>
/// Short-circuits with the artifact <c>short:&lt;note&gt;</c> when its mode is <see cref="InvokedStepModes.Short"/>;
/// otherwise the short circuit fails, which is ignored, and the run ends as <see cref="InvokedStepTrain"/> does.
/// </summary>
public class InvokedShortCircuitTrain
    : ServiceTrain<InvokedStepInput, InvokedStepOutput>,
        IInvokedShortCircuitTrain
{
    protected override Task<Either<Exception, InvokedStepOutput>> Junctions() =>
        Chain<InvokedStepEnter>()
            .ShortCircuit<InvokedStepShortCut>()
            .Chain<InvokedStepFinish>()
            .Resolve();
}
