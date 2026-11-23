using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <inheritdoc cref="IEnqueueContextConsumingTrain"/>
public class EnqueueContextConsumingTrain(IEnqueueContextAccessor enqueueContext)
    : ServiceTrain<EnqueueContextProbeInput, bool>,
        IEnqueueContextConsumingTrain
{
    public IEnqueueContextAccessor EnqueueContext { get; } = enqueueContext;

    protected override Task<Either<Exception, bool>> Junctions() =>
        Chain<EnqueueContextProbeToFlag>().Resolve();
}
