using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainBus;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

public record NestedScopeTrackInput;

public record NestedScopeTrackOutput(Guid ParentScopeMarkerId, Guid ChildScopeMarkerId);

public interface INestedScopeTrackTrain
    : IServiceTrain<NestedScopeTrackInput, NestedScopeTrackOutput>;

/// <summary>
/// Test train that dispatches a child <see cref="ScopeTrackTrain"/> via <see cref="ITrainBus"/>
/// and returns both its own and the child's scope marker IDs.
/// Used to verify nested dispatch creates separate scopes.
/// </summary>
public class NestedScopeTrackTrain
    : ServiceTrain<NestedScopeTrackInput, NestedScopeTrackOutput>,
        INestedScopeTrackTrain
{
    [Inject]
    public ScopeMarker? Marker { get; set; }

    [Inject]
    public ITrainBus? TrainBus { get; set; }

    protected override async Task<Either<Exception, NestedScopeTrackOutput>> RunInternal(
        NestedScopeTrackInput input
    )
    {
        var childResult = await TrainBus!.RunAsync<ScopeTrackOutput>(new ScopeTrackInput());
        return new NestedScopeTrackOutput(Marker!.Id, childResult.ScopeMarkerId);
    }
}
