using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>Input for <see cref="EnqueueContextConsumingTrain"/>.</summary>
public record EnqueueContextProbeInput(string Value);

/// <summary>
/// Takes <see cref="IEnqueueContextAccessor"/> through its constructor, which is what a train
/// does when its <c>OnQueue</c> hook writes through the ambient enqueue context. Registered as
/// a singleton it is the shape that decides whether the enqueue services may be scoped.
/// </summary>
public interface IEnqueueContextConsumingTrain : IServiceTrain<EnqueueContextProbeInput, bool>;

/// <inheritdoc cref="IEnqueueContextConsumingTrain"/>
public class EnqueueContextConsumingTrain(IEnqueueContextAccessor enqueueContext)
    : ServiceTrain<EnqueueContextProbeInput, bool>,
        IEnqueueContextConsumingTrain
{
    public IEnqueueContextAccessor EnqueueContext { get; } = enqueueContext;

    protected override Task<Either<Exception, bool>> Junctions() =>
        Chain<EnqueueContextProbeToFlag>().Resolve();
}

internal sealed class EnqueueContextProbeToFlag : Junction<EnqueueContextProbeInput, bool>
{
    public override Task<bool> Run(EnqueueContextProbeInput input) =>
        Task.FromResult(input.Value.Length > 0);
}
