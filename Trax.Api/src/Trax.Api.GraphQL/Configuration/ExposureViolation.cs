namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// The outcome of evaluating a GraphQL-exposed surface (a <c>[TraxQuery]</c>/<c>[TraxMutation]</c>
/// train or a <c>[TraxQueryModel]</c> entity) against the exposure authorization rule.
/// </summary>
internal enum ExposureViolation
{
    /// <summary>The surface declares a coherent posture; nothing to report.</summary>
    None,

    /// <summary>
    /// The endpoint is open and the surface declares neither <c>[TraxAuthorize]</c> nor
    /// <c>[TraxAllowAnonymous]</c>, so it would be implicitly public.
    /// </summary>
    MissingMarker,

    /// <summary>The surface declares both <c>[TraxAuthorize]</c> and <c>[TraxAllowAnonymous]</c>.</summary>
    Conflict,

    /// <summary>
    /// The surface declares <c>[TraxAllowAnonymous]</c> while the endpoint is gated via
    /// <c>RequireAuthorization()</c>, so the attribute can never take effect.
    /// </summary>
    AnonymousUnderGate,
}
