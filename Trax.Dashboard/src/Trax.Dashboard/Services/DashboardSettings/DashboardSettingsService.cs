using Trax.Dashboard.Services.LocalStorage;
using Trax.Scheduler.Configuration;

namespace Trax.Dashboard.Services.DashboardSettings;

public class DashboardSettingsService(ILocalStorageService localStorage) : IDashboardSettingsService
{
    public const int DefaultPollingIntervalSeconds = 5;
    public const bool DefaultHideAdminTrains = true;
    public const bool DefaultComponentVisibility = true;

    private bool _isInitialized;

    public TimeSpan PollingInterval { get; private set; } =
        TimeSpan.FromSeconds(DefaultPollingIntervalSeconds);

    public DateTime LastPollTime { get; private set; } = DateTime.UtcNow;

    public bool HideAdminTrains { get; private set; } = DefaultHideAdminTrains;

    public IReadOnlyList<string> AdminTrainNames => AdminTrains.ShortNames;

    // Dashboard component visibility (all default to true)
    public bool ShowSummaryCards { get; private set; } = DefaultComponentVisibility;
    public bool ShowExecutionsChart { get; private set; } = DefaultComponentVisibility;
    public bool ShowStatusBreakdown { get; private set; } = DefaultComponentVisibility;
    public bool ShowTopFailures { get; private set; } = DefaultComponentVisibility;
    public bool ShowAvgDuration { get; private set; } = DefaultComponentVisibility;
    public bool ShowRecentFailures { get; private set; } = DefaultComponentVisibility;
    public bool ShowActiveManifests { get; private set; } = DefaultComponentVisibility;
    public bool ShowServerHealth { get; private set; } = DefaultComponentVisibility;
    public bool ShowRealTimeMetrics { get; private set; } = DefaultComponentVisibility;
    public bool ShowThroughputChart { get; private set; } = DefaultComponentVisibility;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        var stored = await localStorage.GetAsync<int?>(StorageKeys.PollingInterval);
        if (stored is > 0)
            PollingInterval = TimeSpan.FromSeconds(stored.Value);

        var hideAdmin = await localStorage.GetAsync<bool?>(StorageKeys.HideAdminTrains);
        if (hideAdmin.HasValue)
            HideAdminTrains = hideAdmin.Value;

        // Component visibility
        ShowSummaryCards = await LoadVisibilityAsync(StorageKeys.ShowSummaryCards);
        ShowExecutionsChart = await LoadVisibilityAsync(StorageKeys.ShowExecutionsChart);
        ShowStatusBreakdown = await LoadVisibilityAsync(StorageKeys.ShowStatusBreakdown);
        ShowTopFailures = await LoadVisibilityAsync(StorageKeys.ShowTopFailures);
        ShowAvgDuration = await LoadVisibilityAsync(StorageKeys.ShowAvgDuration);
        ShowRecentFailures = await LoadVisibilityAsync(StorageKeys.ShowRecentFailures);
        ShowActiveManifests = await LoadVisibilityAsync(StorageKeys.ShowActiveManifests);
        ShowServerHealth = await LoadVisibilityAsync(StorageKeys.ShowServerHealth);
        ShowRealTimeMetrics = await LoadVisibilityAsync(StorageKeys.ShowRealTimeMetrics);
        ShowThroughputChart = await LoadVisibilityAsync(StorageKeys.ShowThroughputChart);

        _isInitialized = true;
    }

    public async Task SetPollingIntervalAsync(int seconds)
    {
        seconds = Math.Max(1, seconds);
        PollingInterval = TimeSpan.FromSeconds(seconds);
        await localStorage.SetAsync(StorageKeys.PollingInterval, seconds);
    }

    public async Task SetHideAdminTrainsAsync(bool hide)
    {
        HideAdminTrains = hide;
        await localStorage.SetAsync(StorageKeys.HideAdminTrains, hide);
    }

    public async Task SetComponentVisibilityAsync(string key, bool visible)
    {
        switch (key)
        {
            case StorageKeys.ShowSummaryCards:
                ShowSummaryCards = visible;
                break;
            case StorageKeys.ShowExecutionsChart:
                ShowExecutionsChart = visible;
                break;
            case StorageKeys.ShowStatusBreakdown:
                ShowStatusBreakdown = visible;
                break;
            case StorageKeys.ShowTopFailures:
                ShowTopFailures = visible;
                break;
            case StorageKeys.ShowAvgDuration:
                ShowAvgDuration = visible;
                break;
            case StorageKeys.ShowRecentFailures:
                ShowRecentFailures = visible;
                break;
            case StorageKeys.ShowActiveManifests:
                ShowActiveManifests = visible;
                break;
            case StorageKeys.ShowServerHealth:
                ShowServerHealth = visible;
                break;
            case StorageKeys.ShowRealTimeMetrics:
                ShowRealTimeMetrics = visible;
                break;
            case StorageKeys.ShowThroughputChart:
                ShowThroughputChart = visible;
                break;
        }

        await localStorage.SetAsync(key, visible);
    }

    public void NotifyPolled()
    {
        LastPollTime = DateTime.UtcNow;
    }

    private async Task<bool> LoadVisibilityAsync(string key)
    {
        var stored = await localStorage.GetAsync<bool?>(key);
        return stored ?? DefaultComponentVisibility;
    }
}
