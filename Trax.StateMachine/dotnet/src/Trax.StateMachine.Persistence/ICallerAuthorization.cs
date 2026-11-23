namespace Trax.StateMachine.Persistence;

/// <summary>
/// The current caller's roles and authorization policies, as the host's authorization evaluates them. The draft
/// operations hold a <see cref="ISnapshotPrincipal"/>, which names only the user; the generic state-machine
/// operations build their <see cref="Actor.Client"/> from both, so an event's <c>Requires</c> and
/// <c>RequiresPolicy</c> rules are checked against the request's own user, per event. Trax.Api registers one over the
/// request's <c>ClaimsPrincipal</c>, matching roles exactly as <c>[TraxAuthorize]</c> does. Without one, a caller
/// holds no role and satisfies no policy, so every event that requires one is refused, and a machine a client can
/// reach that names a policy refuses startup.
/// </summary>
public interface ICallerAuthorization
{
    /// <summary>Whether the current caller holds <paramref name="role"/>, compared ordinally.</summary>
    /// <param name="role">The role an event's rule names.</param>
    bool IsInRole(string role);

    /// <summary>Whether the current caller satisfies the authorization policy named <paramref name="policy"/>.</summary>
    /// <param name="policy">The policy an event's rule names.</param>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    Task<bool> SatisfiesPolicy(string policy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the host registers a policy named <paramref name="policy"/>. The startup check refuses a machine a
    /// client can reach whose rule names a policy no caller could ever satisfy.
    /// </summary>
    /// <param name="policy">The policy an event's or read's rule names.</param>
    Task<bool> IsPolicyRegistered(string policy);
}
