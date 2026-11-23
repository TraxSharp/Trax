using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>Implements the train interface without ServiceTrain, so its run is its own.</summary>
public sealed class BareTrain : IBareTrain
{
    public Metadata? Metadata => null;

    public Task<JobOutput> Run(BareInput input, CancellationToken cancellationToken = default) =>
        Task.FromResult(new JobOutput());

    public void Dispose() { }
}
