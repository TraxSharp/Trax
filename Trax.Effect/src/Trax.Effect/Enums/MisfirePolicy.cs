namespace Trax.Effect.Enums;

/// <summary>
/// Defines how the scheduler handles missed/overdue runs for a manifest.
/// </summary>
/// <remarks>
/// When the scheduler is down or a manifest's scheduled time passes without execution,
/// the misfire policy determines behavior upon recovery. The policy is evaluated in
/// conjunction with the misfire threshold — if the overdue duration is within the threshold,
/// the job fires normally regardless of policy.
/// </remarks>
public enum MisfirePolicy
{
    /// <summary>
    /// If overdue, fire once immediately. This is the default behavior.
    /// </summary>
    FireOnceNow = 0,

    /// <summary>
    /// If overdue beyond the misfire threshold, skip and wait for the next natural
    /// occurrence. For interval-based schedules, the scheduler advances to the most
    /// recent interval boundary and checks whether we are within threshold of it.
    /// </summary>
    DoNothing = 1,
}
