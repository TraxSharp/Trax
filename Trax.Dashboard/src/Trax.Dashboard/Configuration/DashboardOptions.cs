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
}
