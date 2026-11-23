using Trax.Core.Monad;

namespace Trax.Api.DTOs;

/// <summary>
/// One track of a routing node, or one branch of a <c>Parallel</c> node, in a <see cref="RunGraph"/>.
/// </summary>
/// <param name="Name">The track's or branch's name.</param>
/// <param name="Description">What the track is for, as offered to the decider; null for a branch.</param>
/// <param name="IsFallback">True for the <c>Otherwise</c> or <c>Unsure</c> track; never for a branch.</param>
/// <param name="Taken">
/// True when the run took this track. Every branch of a <c>Parallel</c> step runs, so each is taken
/// once the step has started.
/// </param>
/// <param name="Nodes">The track's nodes, in declared order.</param>
public sealed record RunGraphTrack(
    string Name,
    string? Description,
    bool IsFallback,
    bool Taken,
    IReadOnlyList<RunGraphNode> Nodes
);
