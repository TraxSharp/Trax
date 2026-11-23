using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

public interface IInvokedDecidingTrain : IServiceTrain<InvokedStepInput, InvokedStepOutput>;
