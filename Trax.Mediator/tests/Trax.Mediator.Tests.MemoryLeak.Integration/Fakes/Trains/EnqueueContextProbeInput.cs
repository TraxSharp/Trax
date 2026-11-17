using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>Input for <see cref="EnqueueContextConsumingTrain"/>.</summary>
public record EnqueueContextProbeInput(string Value);
