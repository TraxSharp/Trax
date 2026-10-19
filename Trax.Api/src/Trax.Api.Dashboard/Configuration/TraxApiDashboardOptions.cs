namespace Trax.Api.Dashboard.Configuration;

/// <summary>
/// How <c>UseTraxApiDashboard()</c> serves the dashboard: who may load it, and which GraphQL
/// endpoint it talks to.
/// </summary>
/// <remarks>
/// A posture is required: <see cref="RequirePolicy"/>, <see cref="RequireRoles"/> or
/// <see cref="AllowAnonymousShell"/>. Without one, <c>UseTraxApiDashboard()</c> throws at
/// startup. The posture gates the app itself (its page and its scripts); every read and write the
/// app makes goes through the host's Trax GraphQL endpoint, which authorizes it on its own.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/api-dashboard/use-trax-api-dashboard">SDK reference</seealso>
public sealed class TraxApiDashboardOptions
{
    /// <summary>The policy every request for the app must satisfy, if one was named.</summary>
    internal string? Policy { get; private set; }

    /// <summary>The roles a caller must be in one of, if any were named.</summary>
    internal IReadOnlyList<string>? Roles { get; private set; }

    /// <summary>Whether <see cref="AllowAnonymousShell"/> was called.</summary>
    internal bool AnonymousShellAllowed { get; private set; }

    /// <summary>Whether <see cref="HostAuthenticates"/> was called.</summary>
    internal bool HostAuthenticated { get; private set; }

    /// <summary>
    /// The path of the Trax GraphQL endpoint the app talks to, on the host's own origin, such as
    /// <c>/trax/graphql</c>. When unset, the app talks to the path <c>UseTraxGraphQL</c> mapped,
    /// or <c>/trax/graphql</c> when the host maps the endpoint some other way.
    /// </summary>
    public string? GraphQLPath { get; set; }

    internal bool HasAuthorizationPosture =>
        Policy is not null || Roles is not null || AnonymousShellAllowed;

    /// <summary>
    /// Requires the named authorization policy on every request for the app. The policy must be
    /// registered with <c>AddAuthorization</c>; <c>UseTraxApiDashboard()</c> refuses to start
    /// when it is not. Combined with <see cref="RequireRoles"/>, a caller must satisfy both.
    /// </summary>
    /// <remarks>
    /// A browser loading a page sends cookies, not an <c>X-Api-Key</c> or <c>Authorization</c>
    /// header, so the policy needs a scheme the browser carries, such as a cookie or OpenID
    /// Connect sign-in.
    /// </remarks>
    public TraxApiDashboardOptions RequirePolicy(string policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        ThrowIfAnonymous(nameof(RequirePolicy));
        Policy = policy;
        return this;
    }

    /// <summary>
    /// Requires the caller to be in at least one of <paramref name="roles"/> on every request for
    /// the app. Combined with <see cref="RequirePolicy"/>, a caller must satisfy both.
    /// </summary>
    public TraxApiDashboardOptions RequireRoles(params string[] roles)
    {
        if (roles is null || roles.Length == 0 || roles.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException(
                "RequireRoles() needs at least one role, and no blank ones.",
                nameof(roles)
            );
        ThrowIfAnonymous(nameof(RequireRoles));
        Roles = roles;
        return this;
    }

    /// <summary>
    /// Serves the app with no authorization of its own: anyone who can reach the path can load
    /// its page and scripts, unless something outside it (a fallback authorization policy, an
    /// ingress rule) stops them. The app holds no data; everything it shows or changes still goes
    /// through the host's Trax GraphQL endpoint with the credential the operator enters, and that
    /// endpoint authorizes it. <c>UseTraxApiDashboard()</c> logs a warning on every start while
    /// this is set.
    /// </summary>
    public TraxApiDashboardOptions AllowAnonymousShell()
    {
        if (HostAuthenticated)
            throw new InvalidOperationException(
                "AllowAnonymousShell() and HostAuthenticates() contradict each other: with an anonymous "
                    + "shell nothing tells the app the GraphQL endpoint will know the caller. Remove one."
            );
        if (Policy is not null || Roles is not null)
            throw new InvalidOperationException(
                "AllowAnonymousShell() and RequirePolicy()/RequireRoles() contradict each other: "
                    + "the dashboard is either gated or not. Remove one."
            );
        AnonymousShellAllowed = true;
        return this;
    }

    /// <summary>
    /// Says the host already knows who is asking on every request the browser makes, by a cookie or
    /// OpenID Connect sign-in, or a proxy that adds identity headers, so the app asks for no API key
    /// or token: it sends none, on HTTP or in the subscription socket, and the GraphQL endpoint
    /// authorizes each request by the host's own scheme. Use it with <see cref="RequirePolicy"/> or
    /// <see cref="RequireRoles"/>; with <see cref="AllowAnonymousShell"/> it throws, since then
    /// nothing gates the page or vouches that the endpoint will know the caller.
    /// </summary>
    public TraxApiDashboardOptions HostAuthenticates()
    {
        if (AnonymousShellAllowed)
            throw new InvalidOperationException(
                "HostAuthenticates() and AllowAnonymousShell() contradict each other: with an anonymous "
                    + "shell nothing tells the app the GraphQL endpoint will know the caller. Remove one."
            );
        HostAuthenticated = true;
        return this;
    }

    private void ThrowIfAnonymous(string method)
    {
        if (AnonymousShellAllowed)
            throw new InvalidOperationException(
                $"{method}() and AllowAnonymousShell() contradict each other: the dashboard is "
                    + "either gated or not. Remove one."
            );
    }
}
