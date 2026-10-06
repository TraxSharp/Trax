namespace Trax.Dashboard.Configuration;

/// <summary>
/// Settings for the Trax Dashboard, passed to the <c>configure</c> callback of
/// <see cref="Extensions.DashboardServiceExtensions.AddTraxDashboard(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{DashboardOptions}?)"/>
/// and registered as a singleton.
/// </summary>
/// <remarks>
/// <see cref="Extensions.DashboardServiceExtensions.UseTraxDashboard"/> overwrites
/// <see cref="RoutePrefix"/> with its own <c>routePrefix</c> argument and
/// <see cref="EnvironmentName"/> with the host's environment, so both are read-only to a host.
/// </remarks>
public class DashboardOptions
{
    /// <summary>
    /// The prefix the sidebar builds its links from, normalised by <c>UseTraxDashboard</c> to a
    /// single leading slash and no trailing one. Defaults to <c>/trax</c>. The pages themselves are
    /// always routed under <c>/trax</c>, so a different value produces sidebar links that 404.
    /// </summary>
    public string RoutePrefix { get; internal set; } = "/trax";

    /// <summary>
    /// The product name in the dashboard header, which reads "<c>{Title} Dashboard</c>".
    /// Defaults to <c>Trax</c>; the <c>title</c> argument of <c>UseTraxDashboard</c> overrides it.
    /// </summary>
    public string Title { get; set; } = "Trax";

    /// <summary>
    /// The hosting environment name (e.g., "Development", "Production").
    /// Automatically populated from <c>IHostEnvironment.EnvironmentName</c>
    /// when <see cref="Extensions.DashboardServiceExtensions.UseTraxDashboard"/> is called.
    /// </summary>
    public string EnvironmentName { get; internal set; } = "";

    internal string? Policy { get; private set; }

    internal IReadOnlyList<string>? Roles { get; private set; }

    internal bool AnonymousAllowed { get; private set; }

    /// <summary>
    /// How often an established circuit re-checks the posture against the host's current
    /// authentication state, besides re-checking whenever that state changes.
    /// </summary>
    internal TimeSpan AuthorizationRevalidationInterval { get; set; } = TimeSpan.FromMinutes(1);

    internal bool HasAuthorizationPosture =>
        Policy is not null || Roles is not null || AnonymousAllowed;

    /// <summary>
    /// Requires the named authorization policy on every dashboard endpoint: the pages and the
    /// Blazor circuit hub. The policy must be registered with <c>AddAuthorization</c>;
    /// <c>UseTraxDashboard()</c> refuses to start when it is not. Combined with
    /// <see cref="RequireRoles"/>, a caller must satisfy both.
    /// </summary>
    public DashboardOptions RequirePolicy(string policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        ThrowIfAnonymous(nameof(RequirePolicy));
        Policy = policy;
        return this;
    }

    /// <summary>
    /// Requires the caller to be in at least one of <paramref name="roles"/> on every dashboard
    /// endpoint: the pages and the Blazor circuit hub. Combined with
    /// <see cref="RequirePolicy"/>, a caller must satisfy both.
    /// </summary>
    public DashboardOptions RequireRoles(params string[] roles)
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
    /// Maps the dashboard with no authorization of its own. Everyone who can reach the route
    /// can use every page, including the ones that queue, run and cancel trains and change
    /// scheduler settings, unless something outside the dashboard (a fallback authorization
    /// policy, an ingress rule) stops them. <c>UseTraxDashboard()</c> logs a warning on every
    /// start while this is set.
    /// </summary>
    public DashboardOptions AllowAnonymousDashboard()
    {
        if (Policy is not null || Roles is not null)
            throw new InvalidOperationException(
                "AllowAnonymousDashboard() and RequirePolicy()/RequireRoles() contradict each "
                    + "other: the dashboard is either gated or not. Remove one."
            );
        AnonymousAllowed = true;
        return this;
    }

    private void ThrowIfAnonymous(string method)
    {
        if (AnonymousAllowed)
            throw new InvalidOperationException(
                $"{method}() and AllowAnonymousDashboard() contradict each other: the dashboard "
                    + "is either gated or not. Remove one."
            );
    }
}
