using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>
/// Takes <see cref="IEnqueueContextAccessor"/> through its constructor, which is what a train
/// does when its <c>OnQueue</c> hook writes through the ambient enqueue context. Registered as
/// a singleton it is the shape that decides whether the enqueue services may be scoped.
/// </summary>
public interface IEnqueueContextConsumingTrain : IServiceTrain<EnqueueContextProbeInput, bool>;
