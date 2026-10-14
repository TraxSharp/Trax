namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Codes a draft operation is refused with when the cause is the caller or the request rather than the machine.
/// They are part of the wire contract the client twins read.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/result-codes">SDK reference</seealso>
public static class DraftRefusalCodes
{
    /// <summary>The caller does not meet the rule the event, start or read declares.</summary>
    public const string Forbidden = "forbidden";

    /// <summary>The request id was already used for a different request.</summary>
    public const string RequestIdReused = "request-id-reused";

    /// <summary>
    /// The trigger runs the machine's irreversible effect from this state, so only a send fires it, never an advance.
    /// </summary>
    public const string EffectBound = "effect-bound";

    /// <summary>A save is not allowed for this machine, because its states are reached through declared events only.</summary>
    public const string SaveNotAllowed = "save-not-allowed";

    /// <summary>
    /// The instance is in a state only the server puts it in (a committed state, an effect's target, a state that
    /// invokes a train, a state an invoked train's outcome reaches, or the target of an event gated by a role or a
    /// policy), or an effect on it is running or recording its result, so its owner may not discard it.
    /// </summary>
    public const string DiscardNotAllowed = "discard-not-allowed";

    /// <summary>The client runs an older version of the machine than the server, and must reload.</summary>
    public const string ClientOutdated = "client-outdated";

    /// <summary>The client runs a newer version of the machine than this server; a retry may reach a newer one.</summary>
    public const string ServerOutdated = "server-outdated";

    /// <summary>
    /// The user already holds <see cref="StateMachineOptions.MaxDraftsPerUser"/> drafts, so a start or a save
    /// creating another is refused and writes nothing.
    /// </summary>
    public const string TooManyDrafts = "too-many-drafts";

    /// <summary>
    /// An effect is pending on the instance: every other write to it waits until the effect is recorded or has
    /// failed. Retry once it has finished.
    /// </summary>
    public const string EffectInProgress = "effect-in-progress";

    /// <summary>
    /// The request id is missing, empty, whitespace, or longer than <see cref="RequestIdRing.MaxRequestIdLength"/>.
    /// </summary>
    public const string InvalidRequestId = "invalid-request-id";

    /// <summary>The caller names no user, so there is no owner to act for.</summary>
    public const string Unauthenticated = "unauthenticated";

    /// <summary>The machine declares no <c>start</c>, so a client cannot create an instance of it.</summary>
    public const string NoStart = "no-start";

    /// <summary>The instance belongs to the system: only host code creates, drives or deletes it.</summary>
    public const string SystemOwned = "system-owned";

    /// <summary>
    /// The caller has no instance with this id that it may read: a missing id, another user's, a system instance's,
    /// and one the read rule refuses the caller all get this one answer, so it tells the caller nothing about which.
    /// </summary>
    public const string NotFound = "not-found";

    /// <summary>
    /// The caller already holds as many open <c>machineChanged</c> subscriptions as one principal may, on this
    /// instance or in all; one that completes frees its place.
    /// </summary>
    public const string TooManySubscriptions = "too-many-subscriptions";
}
