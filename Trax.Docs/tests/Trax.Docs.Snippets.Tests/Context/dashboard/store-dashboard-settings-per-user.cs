// What the example on dashboard/store-dashboard-settings-per-user.md takes as given: the
// implementation it registers.
using Trax.Dashboard.Services.DashboardSettings;

public class PerUserDashboardSettings : IDashboardSettingsService
{
    public TimeSpan PollingInterval { get; private set; } = TimeSpan.FromSeconds(5);

    public DateTime LastPollTime { get; private set; }

    public bool HideAdminTrains { get; private set; } = true;

    public bool ShowSummaryCards => true;

    public bool ShowExecutionsChart => true;

    public bool ShowFailures => true;

    public bool ShowAvgDuration => true;

    public bool ShowServerHealth => true;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task SetPollingIntervalAsync(int seconds)
    {
        PollingInterval = TimeSpan.FromSeconds(Math.Max(1, seconds));
        return Task.CompletedTask;
    }

    public Task SetHideAdminTrainsAsync(bool hide)
    {
        HideAdminTrains = hide;
        return Task.CompletedTask;
    }

    public Task SetComponentVisibilityAsync(string key, bool visible) => Task.CompletedTask;

    public void NotifyPolled() => LastPollTime = DateTime.UtcNow;
}
