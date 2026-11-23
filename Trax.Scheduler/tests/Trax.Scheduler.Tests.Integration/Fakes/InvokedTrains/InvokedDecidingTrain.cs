using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>
/// Asks a decider which lane to take. The express lane fails, the ground lane succeeds with the artifact
/// <c>lane:ground:&lt;note&gt;</c>.
/// </summary>
public class InvokedDecidingTrain
    : ServiceTrain<InvokedStepInput, InvokedStepOutput>,
        IInvokedDecidingTrain
{
    protected override Task<Either<Exception, InvokedStepOutput>> Junctions() =>
        Switch<InvokedStepInput, InvokedLane>(tracks =>
                tracks
                    .When(InvokedLane.Express, t => t.Chain<InvokedTakeExpress>())
                    .When(InvokedLane.Ground, t => t.Chain<InvokedTakeGround>())
            )
            .Chain<InvokedLaneFinish>()
            .Resolve();
}
