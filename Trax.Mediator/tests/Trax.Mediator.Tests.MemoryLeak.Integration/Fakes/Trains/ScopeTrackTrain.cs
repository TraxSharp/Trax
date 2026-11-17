using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>
/// Test train that captures its scope's <see cref="ScopeMarker"/> ID in the output.
/// Used to verify that each <c>RunAsync</c> call creates a separate DI scope.
/// </summary>
public class ScopeTrackTrain : ServiceTrain<ScopeTrackInput, ScopeTrackOutput>, IScopeTrackTrain
{
    [Inject]
    public ScopeMarker? Marker { get; set; }

    [Inject]
    public DisposalTracker? Tracker { get; set; }

    protected override Task<Either<Exception, ScopeTrackOutput>> Junctions() =>
        Task.FromResult<Either<Exception, ScopeTrackOutput>>(
            new ScopeTrackOutput(Marker!.Id, Tracker!)
        );
}
