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

/// <summary>
/// The single source of truth for GraphQL exposure authorization. A surface exposed via
/// GraphQL must declare its authorization posture explicitly so anonymous access is always
/// deliberate, never the result of a forgotten gate. The same rule applies to trains and to
/// query-model entities; only the message label differs.
/// </summary>
internal static class ExposureAuthorizationRule
{
    /// <summary>
    /// Evaluates a single exposed surface. <paramref name="endpointGated"/> is whether the
    /// GraphQL endpoint opted into <c>RequireAuthorization()</c>.
    /// </summary>
    public static ExposureViolation Evaluate(
        bool hasAuthorize,
        bool hasAllowAnonymous,
        bool endpointGated
    )
    {
        // Both markers contradict each other regardless of endpoint posture.
        if (hasAuthorize && hasAllowAnonymous)
            return ExposureViolation.Conflict;

        if (endpointGated)
        {
            // The endpoint already rejects unauthenticated callers at the HTTP layer, so
            // [TraxAllowAnonymous] is a promise it structurally cannot keep. A surface with
            // no marker (or with [TraxAuthorize], which layers a finer policy on top) is fine.
            return hasAllowAnonymous
                ? ExposureViolation.AnonymousUnderGate
                : ExposureViolation.None;
        }

        // The endpoint is open: every exposed surface must state its posture explicitly.
        if (!hasAuthorize && !hasAllowAnonymous)
            return ExposureViolation.MissingMarker;

        return ExposureViolation.None;
    }

    /// <summary>
    /// Builds the host-startup failure message for a violation. <paramref name="surfaceLabel"/>
    /// describes the kind of surface (e.g. "GraphQL-exposed train", "[TraxQueryModel] entity")
    /// and <paramref name="surfaceName"/> is its full type name.
    /// </summary>
    public static string BuildMessage(
        string surfaceLabel,
        string surfaceName,
        ExposureViolation violation
    ) =>
        violation switch
        {
            ExposureViolation.MissingMarker =>
                $"{surfaceLabel} '{surfaceName}' is exposed via GraphQL but declares neither "
                    + "[TraxAuthorize] nor [TraxAllowAnonymous]. An exposed surface must state its "
                    + "authorization posture explicitly: add [TraxAuthorize] (optionally with a "
                    + "policy or roles) to gate it, or [TraxAllowAnonymous] to open it to anonymous "
                    + "callers. To gate the entire endpoint instead, call "
                    + "UseTraxGraphQL(configure: e => e.RequireAuthorization(...)).",
            ExposureViolation.Conflict =>
                $"{surfaceLabel} '{surfaceName}' declares both [TraxAllowAnonymous] and "
                    + "[TraxAuthorize] (directly or via base/interface). The two are mutually "
                    + "exclusive: [TraxAllowAnonymous] opens the surface to anonymous callers, while "
                    + "[TraxAuthorize] gates it. Pick one.",
            ExposureViolation.AnonymousUnderGate =>
                $"{surfaceLabel} '{surfaceName}' declares [TraxAllowAnonymous], but the GraphQL "
                    + "endpoint is gated via RequireAuthorization(). The endpoint rejects "
                    + "unauthenticated callers at the HTTP layer before the surface is reached, so "
                    + "[TraxAllowAnonymous] can never take effect. Remove [TraxAllowAnonymous], or "
                    + "drop the endpoint-level RequireAuthorization() if this surface should be "
                    + "publicly reachable.",
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, null),
        };
}
