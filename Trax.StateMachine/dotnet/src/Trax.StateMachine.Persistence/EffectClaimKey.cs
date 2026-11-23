namespace Trax.StateMachine.Persistence;

/// <summary>
/// The key an irreversible effect claims for one draft or instance. A user's is <c>{prefix}:{userKey}:{id}</c>; a
/// system instance's is <c>{prefix}::{id}</c>, which no user's can equal, because a user key is never empty. So a
/// user holding the same id as a system instance never shares its claims.
/// </summary>
internal static class EffectClaimKey
{
    /// <summary>The claim key of a user's draft.</summary>
    public static string ForUser(string prefix, string userKey, Guid id) =>
        $"{prefix}:{userKey}:{id}";

    /// <summary>The claim key of a system-owned instance.</summary>
    public static string ForSystem(string prefix, Guid id) => $"{prefix}::{id}";
}
