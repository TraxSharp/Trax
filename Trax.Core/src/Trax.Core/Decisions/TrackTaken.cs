namespace Trax.Core.Decisions;

/// <summary>
/// Which track a <c>Switch</c>, <c>Gate</c> or <c>Scale</c> sent the train down, put in Memory
/// before the track runs.
/// </summary>
/// <typeparam name="TKey">The enum or marker type the routing was on.</typeparam>
/// <param name="Track">The track's name: an enum member, <c>Yes</c>, <c>No</c>, <c>Unsure</c> or <c>Otherwise</c>.</param>
/// <param name="FallbackReason">Why the decision was not followed, or null when it was.</param>
public sealed record TrackTaken<TKey>(string Track, string? FallbackReason)
{
    /// <summary>True when the decision was not followed and a fallback track was taken.</summary>
    public bool TookFallback => FallbackReason is not null;
}
