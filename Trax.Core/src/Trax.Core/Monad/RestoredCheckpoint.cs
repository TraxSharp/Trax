using System.Diagnostics.CodeAnalysis;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

/// <summary>A stored checkpoint, read back.</summary>
/// <param name="NodeId">The checkpoint's node id.</param>
/// <param name="StateType">The declared state type, which it goes into Memory under.</param>
/// <param name="State">The state.</param>
/// <param name="Tracks">The routes taken before it, as <see cref="TrackTaken{TKey}"/> values.</param>
internal sealed record RestoredCheckpoint(
    string NodeId,
    Type StateType,
    object State,
    IReadOnlyList<object> Tracks
);
