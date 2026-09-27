namespace Trax.Effect.Data.Testing;

/// <summary>
/// What <see cref="DataLayerGuards.OwnerScopeCompleteness"/> needs to know about an application's
/// owners. Trax owns the mechanism (a row filter that reads the principal, and a bare
/// <c>[TraxAuthorize]</c> on anything exposed); the owner type and the accessor are the
/// application's.
/// </summary>
public sealed record OwnerScopeCensusOptions
{
    /// <summary>
    /// The entity rows belong to, such as a user profile. A foreign key whose principal is this
    /// type (or derives from it) marks the dependent as per-user, and this entity is its own
    /// owner's row.
    /// </summary>
    public required Type OwnerType { get; init; }

    /// <summary>
    /// The type a row filter reads the current principal through. A filter whose expression
    /// references a value of this type (or one implementing it) is an owner-scope filter; any
    /// other filter, such as a soft-delete rule, is not.
    /// </summary>
    public required Type PrincipalAccessorType { get; init; }

    /// <summary>
    /// Scalar properties that hold an owner id without a modelled foreign key, such as
    /// <c>UserId</c> on a table that never navigates to the owner. Matched by property name on
    /// the EF model. Empty by default, because a name is a guess and the census should not make it
    /// for you.
    /// </summary>
    public IReadOnlySet<string> OwnerIdProperties { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Entities that hold per-user data but reach their owner only through a navigation, each with
    /// the reason. Their principal-reading filter is the only thing that marks them as per-user,
    /// so deleting it would drop them out of the census rather than fail it. Naming them here is
    /// the second witness, checked in both directions: a named entity must still carry the filter,
    /// and a filtered entity with no owner key must be named.
    /// </summary>
    public IReadOnlyDictionary<Type, string> NavigationScoped { get; init; } =
        new Dictionary<Type, string>();

    /// <summary>
    /// Per-user entities deliberately left out of the census, each with a written reason. An entry
    /// with a blank reason, or one naming an entity the census would not flag, is itself reported.
    /// </summary>
    public IReadOnlyDictionary<Type, string> Exemptions { get; init; } =
        new Dictionary<Type, string>();

    /// <summary>
    /// Per-user <c>[TraxQueryModel]</c> entities allowed to carry a role or policy on their
    /// <c>[TraxAuthorize]</c>, each with the reason. A gate only ever adds restriction: every
    /// other check still applies, the principal-reading filter included, and
    /// <c>[TraxAllowAnonymous]</c> or a missing <c>[TraxAuthorize]</c> is refused as before. An
    /// entry is itself reported when its reason is blank, when the entity is also exempted, and
    /// when it is stale: the entity is not per-user, is not exposed, or carries no role or policy.
    /// </summary>
    public IReadOnlyDictionary<Type, string> Gated { get; init; } = new Dictionary<Type, string>();
}
