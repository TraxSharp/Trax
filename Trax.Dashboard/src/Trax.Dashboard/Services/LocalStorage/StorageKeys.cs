namespace Trax.Dashboard.Services.LocalStorage;

/// <summary>
/// The browser <c>localStorage</c> keys the dashboard writes its per-browser preferences under.
/// Infrastructure used by the dashboard's own services; not intended to be used directly.
/// </summary>
internal static class StorageKeys
{
    /// <summary>The selected Radzen theme name, for example <c>material</c> or <c>material-dark</c>.</summary>
    public const string Theme = "trax-theme";

    /// <summary>Whether the sidebar is expanded (JSON boolean).</summary>
    public const string SidebarExpanded = "trax-sidebar-expanded";

    /// <summary>The data refresh interval in whole seconds (JSON number).</summary>
    public const string PollingInterval = "trax-polling-interval";

    /// <summary>Whether administrative scheduler trains are hidden (JSON boolean).</summary>
    public const string HideAdminTrains = "trax-hide-admin-trains";

    /// <summary>Whether the home page summary cards are shown (JSON boolean).</summary>
    public const string ShowSummaryCards = "trax-show-summary-cards";

    /// <summary>Whether the home page executions chart is shown (JSON boolean).</summary>
    public const string ShowExecutionsChart = "trax-show-executions-chart";

    /// <summary>Whether the home page top-failures panel is shown (JSON boolean).</summary>
    public const string ShowFailures = "trax-show-failures";

    /// <summary>Whether the home page average-duration panel is shown (JSON boolean).</summary>
    public const string ShowAvgDuration = "trax-show-avg-duration";

    /// <summary>Whether the home page server health panel is shown (JSON boolean).</summary>
    public const string ShowServerHealth = "trax-show-server-health";
}
