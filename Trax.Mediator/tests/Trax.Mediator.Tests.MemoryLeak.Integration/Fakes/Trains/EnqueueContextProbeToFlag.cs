using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

internal sealed class EnqueueContextProbeToFlag : Junction<EnqueueContextProbeInput, bool>
{
    public override Task<bool> Run(EnqueueContextProbeInput input) =>
        Task.FromResult(input.Value.Length > 0);
}
