namespace Trax.Core.Monad;

/// <summary>
/// One track a routing step declares, with the steps it runs recorded on their own.
/// </summary>
/// <param name="Name">The track's name: an enum member, <c>Yes</c>, <c>No</c>, <c>Unsure</c> or <c>Otherwise</c>.</param>
/// <param name="Description">What the track is for, as offered to the decider.</param>
/// <param name="IsFallback">True for the <c>Otherwise</c> or <c>Unsure</c> track.</param>
/// <param name="Steps">The track's chain, recorded the way a train's chain is.</param>
public sealed record ChainTrack(
    string Name,
    string? Description,
    bool IsFallback,
    ChainRecorder Steps
);
