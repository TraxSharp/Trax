namespace Trax.StateMachine.Persistence;

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

    /// <summary>The request's caller authorization, when the host registers one (Trax.Api does), or <see langword="null"/>.</summary>
    public static ICallerAuthorization? Caller(IServiceProvider? services) =>
        services?.GetService(typeof(ICallerAuthorization)) as ICallerAuthorization;

    /// <summary>
    /// The user a draft operation acts for: a client's key, or <see langword="null"/> when it names none (an empty or
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
            if (!await client.SatisfiesPolicy(policy, cancellationToken).ConfigureAwait(false))
                return false;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="client"/> may read its own instances of a machine with <paramref name="rules"/>: its
    /// <c>get</c> rule when it declares one, as <c>get</c> checks it; an owner reads a machine that declares none. A
    /// view holds each machine it reads to the same check.
    /// </summary>
    public static Task<bool> MayRead(
        Actor.Client client,
        MachineRules rules,
        CancellationToken cancellationToken
    ) => rules.Get is { } rule ? Admits(client, rule, cancellationToken) : Task.FromResult(true);

    /// <summary>The refusal of a caller a rule does not admit.</summary>
    public static AdvanceOutcome.Rejected Forbidden() =>
        new(RefusalCode.Forbidden, "You may not do this.");
}
