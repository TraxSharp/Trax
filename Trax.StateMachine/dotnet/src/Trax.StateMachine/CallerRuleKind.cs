namespace Trax.StateMachine;

/// <summary>Which kind of caller a <see cref="CallerRule"/> admits.</summary>
internal enum CallerRuleKind
{
    /// <summary>Any caller, signed in or not.</summary>
    Anonymous,

    /// <summary>Any authenticated caller.</summary>
    AnyAuthenticated,

    /// <summary>Only the instance's owner (reads only), and the requirements beside it.</summary>
    OwnerOnly,

    /// <summary>An authenticated caller who holds a role of every set and satisfies every policy.</summary>
    Requires,
}
