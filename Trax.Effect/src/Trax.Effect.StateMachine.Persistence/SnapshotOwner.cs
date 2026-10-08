namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The rule for who owns a draft. A null, empty or whitespace user key is no owner at all: accepting one would
/// give every caller mapped to it one shared owner, able to read and advance each other's drafts.
/// </summary>
internal static class SnapshotOwner
{
    /// <summary>
    /// The owner the request's drafts are scoped to, or null when the principal names none. The draft mutations
    /// refuse a null result as <c>unauthenticated</c>.
    /// </summary>
    public static string? Key(ISnapshotPrincipal principal) =>
        principal.CurrentUserKey is { } key && !string.IsNullOrWhiteSpace(key) ? key : null;

    /// <summary>Refuses a key that names no owner before any draft is read or written.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="userKey"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="userKey"/> is empty or whitespace.</exception>
    public static void Require(string userKey) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);
}

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
