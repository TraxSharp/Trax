namespace Trax.Dashboard.Services.DashboardSettings;

public interface IDashboardSettingsService
{
    TimeSpan PollingInterval { get; }
    DateTime LastPollTime { get; }
    bool HideAdminWorkflows { get; }
    IReadOnlyList<string> AdminWorkflowNames { get; }
    Task InitializeAsync();
    Task SetPollingIntervalAsync(int seconds);
    Task SetHideAdminWorkflowsAsync(bool hide);
    void NotifyPolled();

    // Dashboard component visibility
    bool ShowSummaryCards { get; }
    bool ShowExecutionsChart { get; }
    bool ShowStatusBreakdown { get; }
    bool ShowTopFailures { get; }
    bool ShowAvgDuration { get; }
    bool ShowRecentFailures { get; }
    bool ShowActiveManifests { get; }
    bool ShowServerHealth { get; }
    Task SetComponentVisibilityAsync(string key, bool visible);
}
