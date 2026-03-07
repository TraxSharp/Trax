namespace Trax.Dashboard.Services.LocalStorage;

public static class StorageKeys
{
    public const string Theme = "trax-theme";
    public const string SidebarExpanded = "trax-sidebar-expanded";
    public const string PollingInterval = "trax-polling-interval";
    public const string HideAdminTrains = "trax-hide-admin-trains";

    // Dashboard component visibility
    public const string ShowSummaryCards = "trax-show-summary-cards";
    public const string ShowExecutionsChart = "trax-show-executions-chart";
    public const string ShowFailures = "trax-show-failures";
    public const string ShowAvgDuration = "trax-show-avg-duration";
    public const string ShowServerHealth = "trax-show-server-health";
}
