namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Who is acting on a user's machine instance: always a <see cref="Client"/>, whose every event is checked against the
/// event's declared rule. Host code acting on a user's instance is a client too, holding the roles it is given (none,
/// through the <c>userKey</c> overloads); there is no trusted actor for a user's instance. Host code drives a
/// system-owned instance through <see cref="IMachineInstances"/>, which takes no actor.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/persistence-ports#actors-start-and-discard">SDK reference</seealso>
public abstract record Actor
{
    /// <summary>
    /// A caller acting through a client surface: the user it acts for, and how to check its roles and
    /// authorization policies against an event's rule.
    /// </summary>
    /// <param name="UserKey">The authenticated user's key, the owner of the drafts it reaches.</param>
    /// <param name="IsInRole">Whether the caller holds a role.</param>
    /// <param name="SatisfiesPolicy">Whether the caller satisfies a named authorization policy.</param>
    public sealed record Client(
        string UserKey,
        Func<string, bool> IsInRole,
        Func<string, CancellationToken, Task<bool>> SatisfiesPolicy
    ) : Actor
    {
        /// <summary>
        /// A caller acting for <paramref name="userKey"/> who holds exactly <paramref name="roles"/> and satisfies
        /// no authorization policy.
        /// </summary>
        /// <param name="userKey">The authenticated user's key.</param>
        /// <param name="roles">The roles the caller holds.</param>
        public Client(string userKey, params string[] roles)
            : this(
                userKey,
                new HashSet<string>(roles, StringComparer.Ordinal).Contains,
                static (_, _) => Task.FromResult(false)
            ) { }

        private protected override void OnlyTraxDeclaresAKind() { }
    }

    private Actor() { }

    // Closes the hierarchy. An unsealed record's copy constructor must be protected, so outside code could call it
    // from a record of its own; it cannot implement a member it cannot see, so such a record does not compile.
    private protected abstract void OnlyTraxDeclaresAKind();
}
