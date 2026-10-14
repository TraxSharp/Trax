namespace Trax.Effect.Attributes;

/// <summary>
/// Specifies authorization requirements for a train when executed via the Trax API.
/// </summary>
/// <remarks>
/// When a train is executed through the Trax API (<c>ITrainExecutionService</c>, which the
/// GraphQL mutations and queries call), the framework checks for this attribute and enforces
/// the specified authorization requirements against the current HTTP user before allowing
/// execution.
///
/// Trains without this attribute have no per-train authorization requirements (though an
/// endpoint gate, <c>RequireAuthorization</c> on the GraphQL builder, still applies). A train
/// exposed over GraphQL on an open endpoint must carry this attribute or
/// <see cref="TraxAllowAnonymousAttribute"/>, or the host refuses to start.
/// <para>
/// Combinator semantics when the attribute is present:
/// <list type="bullet">
/// <item>Bare <c>[TraxAuthorize]</c> (no policy, no roles) requires an authenticated user.</item>
/// <item>Policies across all applied attributes are AND'd: every <see cref="Policy"/> must pass.</item>
/// <item>Within a single attribute, <see cref="Roles"/> is a comma-separated list that is OR'd: the user must hold at least one of the listed roles.</item>
/// <item>Roles across applied attributes are AND'd: each attribute's list is a requirement of its own, so <c>[TraxAuthorize(Roles = "A")]</c> with <c>[TraxAuthorize(Roles = "B")]</c> requires both roles. List them in one attribute (<c>Roles = "A,B"</c>) to accept either.</item>
/// <item>When policies and roles are both specified, both sides must be satisfied.</item>
/// </list>
/// </para>
/// The scheduler bypasses this check entirely since it is trusted infrastructure.
/// Authorization is enforced once at API submission time; scheduled and remote-worker
/// executions run against work that was already authorized.
/// <para>
/// <b>On a method</b> it declares the posture of a single GraphQL field: a resolver on an
/// <c>[ExtendObjectType]</c> class adds a field to a type Trax owns, and when the parent type
/// has no gate to inherit, that field has to state its own. Trax.Api emits the matching
/// <c>@authorize</c> directive for it, so the combinator semantics above are identical whether
/// the attribute sits on a train, an entity or a resolver.
/// </para>
/// <para>
/// Placed on an <c>[ExtendObjectType]</c> <i>class</i>, it applies to every field that
/// extension contributes. It deliberately does not apply to the type being extended, which is
/// what HotChocolate's own attribute does and which silently re-gates somebody else's type.
/// </para>
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/attributes/trax-authorize">SDK reference</seealso>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method,
    AllowMultiple = true,
    Inherited = true
)]
public class TraxAuthorizeAttribute : Attribute
{
    /// <summary>
    /// The name of an ASP.NET Core authorization policy that must be satisfied.
    /// </summary>
    public string? Policy { get; init; }

    /// <summary>
    /// A comma-separated list of roles. The user must have at least one of these roles.
    /// </summary>
    public string? Roles { get; init; }

    /// <summary>
    /// Creates a bare <see cref="TraxAuthorizeAttribute"/> with no policy and no roles, which requires only an
    /// authenticated caller. Set <see cref="Policy"/> or <see cref="Roles"/> through the initializer to narrow it.
    /// </summary>
    public TraxAuthorizeAttribute() { }

    /// <summary>
    /// Creates a new <see cref="TraxAuthorizeAttribute"/> requiring the specified policy.
    /// </summary>
    /// <param name="policy">The authorization policy name.</param>
    public TraxAuthorizeAttribute(string policy) => Policy = policy;
}
