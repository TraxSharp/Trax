namespace Trax.Core.Decisions;

/// <summary>
/// One routing step's outcome.
/// </summary>
/// <param name="Train">The train that was routed.</param>
/// <param name="RunId">The run's external id.</param>
/// <param name="On">The enum or marker type the routing was on.</param>
/// <param name="Track">The track taken.</param>
/// <param name="FallbackReason">Why the decision was not followed, or null when it was.</param>
public sealed record TrackRouted(
    string Train,
    string RunId,
    Type On,
    string Track,
    string? FallbackReason
);
