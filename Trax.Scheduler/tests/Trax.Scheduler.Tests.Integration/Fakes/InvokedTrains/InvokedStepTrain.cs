using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>Two effect junctions, so the run reads its cancel flag between them.</summary>
public class InvokedStepTrain : ServiceTrain<InvokedStepInput, InvokedStepOutput>, IInvokedStepTrain
{
    protected override Task<Either<Exception, InvokedStepOutput>> Junctions() =>
        Chain<InvokedStepEnter>().Chain<InvokedStepFinish>().Resolve();
}
