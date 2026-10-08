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
