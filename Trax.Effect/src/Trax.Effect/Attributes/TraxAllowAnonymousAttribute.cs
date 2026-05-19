namespace Trax.Effect.Attributes;

/// <summary>
/// Opens a <see cref="TraxQueryModelAttribute"/>-decorated entity to anonymous
/// GraphQL access. Mirrors HotChocolate's <c>[AllowAnonymous]</c> in spirit:
/// the directly decorated entity is reachable without authentication, while
/// any navigation property whose target type carries
/// <see cref="TraxAuthorizeAttribute"/> still enforces that gate.
/// </summary>
/// <remarks>
/// The cascade does not break: HotChocolate's <c>@authorize</c> directive lives
/// on the target type, so reaching a gated entity through an anonymous parent
/// still rejects the request. The contract is intentionally local — the
/// attribute opens this entity, nothing else.
/// <para>
/// Mutually exclusive with <see cref="TraxAuthorizeAttribute"/>. Declaring both
/// on the same entity (directly or via inheritance) fails at
/// <c>TraxGraphQLBuilder.Build()</c> with a message naming the entity.
/// </para>
/// <para>
/// Endpoint-level authorization is unaffected. If the GraphQL endpoint itself
/// is gated (e.g. <c>UseTraxGraphQL(...).RequireAuthorization(...)</c>),
/// requests are rejected at the HTTP layer before HotChocolate ever runs,
/// matching the behavior of HotChocolate's own <c>[AllowAnonymous]</c>.
/// </para>
/// <para>
/// Entities decorated with this attribute are excluded from the "ungated
/// model surface" warning emitted at host start by the model-exposure warning
/// service. Opting in is explicit, so no nagging.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Interface,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class TraxAllowAnonymousAttribute : Attribute { }
