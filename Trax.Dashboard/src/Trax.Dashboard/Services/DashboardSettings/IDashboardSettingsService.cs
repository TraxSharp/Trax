namespace Trax.Dashboard.Services.DashboardSettings;

public interface IDashboardSettingsService
{
    TimeSpan PollingInterval { get; }
    DateTime LastPollTime { get; }
    bool HideAdminTrains { get; }
    IReadOnlyList<string> AdminTrainNames { get; }
    Task InitializeAsync();
    Task SetPollingIntervalAsync(int seconds);
    Task SetHideAdminTrainsAsync(bool hide);
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
    bool ShowRealTimeMetrics { get; }
    bool ShowThroughputChart { get; }
    Task SetComponentVisibilityAsync(string key, bool visible);
}
