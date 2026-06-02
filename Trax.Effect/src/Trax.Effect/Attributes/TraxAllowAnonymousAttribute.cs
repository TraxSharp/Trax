namespace Trax.Effect.Attributes;

/// <summary>
/// Declares a GraphQL-exposed surface as intentionally public. Apply it to a
/// <see cref="TraxQueryModelAttribute"/>-decorated entity or to a train carrying
/// <see cref="TraxQueryAttribute"/> / <see cref="TraxMutationAttribute"/>. It is the
/// explicit opt-in counterpart to <see cref="TraxAuthorizeAttribute"/>: an exposed
/// surface must declare one or the other, so anonymous access is always a deliberate
/// choice rather than the result of a forgotten gate.
/// </summary>
/// <remarks>
/// On a query-model entity it also mirrors HotChocolate's <c>[AllowAnonymous]</c>:
/// the directly decorated entity is reachable without authentication, while any
/// navigation property whose target type carries <see cref="TraxAuthorizeAttribute"/>
/// still enforces that gate. The cascade does not break, because HotChocolate's
/// <c>@authorize</c> directive lives on the target type, so reaching a gated entity
/// through an anonymous parent still rejects the request.
/// <para>
/// On a train the attribute carries no runtime directive (train authorization is
/// enforced imperatively, not via schema directives). Its role is to satisfy the
/// exposure check: a <c>[TraxQuery]</c>/<c>[TraxMutation]</c> train that declares
/// neither this attribute nor <see cref="TraxAuthorizeAttribute"/> fails host startup.
/// </para>
/// <para>
/// Mutually exclusive with <see cref="TraxAuthorizeAttribute"/>. Declaring both on the
/// same surface (directly or via inheritance) fails at host startup with a message
/// naming it.
/// </para>
/// <para>
/// Contradictory with an endpoint-level gate. If the GraphQL endpoint is gated (e.g.
/// <c>UseTraxGraphQL(configure: e =&gt; e.RequireAuthorization(...))</c>), the HTTP layer
/// rejects unauthenticated callers before the surface is ever reached, so this attribute
/// can never take effect. Declaring it on any exposed surface while the endpoint is gated
/// fails host startup; remove one or the other.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Interface,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class TraxAllowAnonymousAttribute : Attribute { }
