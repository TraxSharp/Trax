namespace Trax.Effect.StateMachine.Persistence;

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

/// <summary>How an <see cref="Actor"/> is checked against a <see cref="CallerRule"/>.</summary>
internal static class CallerRules
{
    /// <summary>
    /// The client a request acts as: the principal's user, with the roles and policies <paramref name="caller"/>
    /// evaluates, read when an event is checked, never cached. With no <paramref name="caller"/>, the client holds
    /// no role and satisfies no policy.
    /// </summary>
    public static Actor.Client Client(string userKey, ICallerAuthorization? caller) =>
        caller is null
            ? new Actor.Client(userKey)
            : new Actor.Client(userKey, caller.IsInRole, caller.SatisfiesPolicy);

    /// <summary>The request's caller authorization, when the host registers one (Trax.Api does), or <c>null</c>.</summary>
    public static ICallerAuthorization? Caller(IServiceProvider? services) =>
        services?.GetService(typeof(ICallerAuthorization)) as ICallerAuthorization;

    /// <summary>
    /// The user a draft operation acts for: a client's key, or <c>null</c> when it names none (an empty or
    /// whitespace key), which is refused.
    /// </summary>
    public static string? UserOf(Actor actor) =>
        actor is Actor.Client { UserKey: var key } && !string.IsNullOrWhiteSpace(key) ? key : null;

    /// <summary>Whether <paramref name="client"/> meets <paramref name="rule"/>. Owner scoping is the caller's.</summary>
    public static async Task<bool> Admits(
        Actor.Client client,
        CallerRule rule,
        CancellationToken cancellationToken
    )
    {
        var authenticated = !string.IsNullOrWhiteSpace(client.UserKey);
        switch (rule.Kind)
        {
            case CallerRuleKind.Anonymous:
                return true;
            case CallerRuleKind.AnyAuthenticated:
                return authenticated;
        }
        if (!authenticated)
            return false;
        foreach (var set in rule.RoleSets)
            if (!set.Any(client.IsInRole))
                return false;
        foreach (var policy in rule.Policies)
            if (!await client.SatisfiesPolicy(policy, cancellationToken))
                return false;
        return true;
    }

    /// <summary>The refusal of a caller a rule does not admit.</summary>
    public static AdvanceOutcome.Rejected Forbidden() =>
        new(DraftRefusalCodes.Forbidden, "You may not do this.");
}
