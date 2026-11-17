using System.Diagnostics.CodeAnalysis;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

/// <summary>A checkpoint a run reached, as the store is handed it.</summary>
/// <param name="Train">The train running, for a store that records which run took it.</param>
/// <param name="ExternalId">The run's external id.</param>
/// <param name="NodeId">The checkpoint's node id, with the branch's path inside it.</param>
/// <param name="BranchPath">The <c>Parallel</c> branch it is in, or null.</param>
/// <param name="StateType">The state type the chain declares, which the value is stored as.</param>
/// <param name="State">The state in Memory.</param>
/// <param name="Tracks">Every <see cref="TrackTaken{TKey}"/> in Memory: the routes taken before it.</param>
/// <param name="Services">The scope the step runs in, for a store that must look at it.</param>
internal sealed record CheckpointTaken(
    object Train,
    string ExternalId,
    string NodeId,
    string? BranchPath,
    Type StateType,
    object State,
    IReadOnlyList<object> Tracks,
    IServiceProvider? Services
);
