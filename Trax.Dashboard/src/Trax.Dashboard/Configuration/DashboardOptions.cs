namespace Trax.Dashboard.Configuration;

public class DashboardOptions
{
    /// <summary>
    /// The URL prefix where the dashboard is mounted (e.g., "/trax").
    /// </summary>
    public string RoutePrefix { get; set; } = "/trax";

    /// <summary>
    /// Title displayed in the dashboard header.
    /// </summary>
    public string Title { get; set; } = "Trax";

    /// <summary>
    /// The hosting environment name (e.g., "Development", "Production").
    /// Automatically populated from <c>IHostEnvironment.EnvironmentName</c>
    /// when <see cref="Extensions.DashboardServiceExtensions.UseTraxDashboard"/> is called.
    /// </summary>
    public string EnvironmentName { get; set; } = "";

    internal string? Policy { get; private set; }

    internal IReadOnlyList<string>? Roles { get; private set; }

    internal bool AnonymousAllowed { get; private set; }

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
