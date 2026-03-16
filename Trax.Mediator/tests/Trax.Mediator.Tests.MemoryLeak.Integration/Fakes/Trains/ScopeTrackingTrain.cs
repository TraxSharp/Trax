using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>
/// Scoped marker service whose unique ID identifies a DI scope.
/// Register as Scoped — each child scope gets a distinct instance.
/// </summary>
public class ScopeMarker
{
    public Guid Id { get; } = Guid.NewGuid();
}

/// <summary>
/// Scoped service that tracks whether it has been disposed.
/// Register as Scoped — scope disposal triggers <see cref="Dispose"/>.
/// </summary>
public class DisposalTracker : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}

public record ScopeTrackInput;

public record ScopeTrackOutput(Guid ScopeMarkerId, DisposalTracker Tracker);

public interface IScopeTrackTrain : IServiceTrain<ScopeTrackInput, ScopeTrackOutput>;

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

    protected override Task<Either<Exception, ScopeTrackOutput>> RunInternal(
        ScopeTrackInput input
    ) =>
        Task.FromResult<Either<Exception, ScopeTrackOutput>>(
            new ScopeTrackOutput(Marker!.Id, Tracker!)
        );
}
