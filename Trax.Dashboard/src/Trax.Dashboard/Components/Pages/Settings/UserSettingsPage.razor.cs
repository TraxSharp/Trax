using Microsoft.AspNetCore.Components;
using Radzen;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;

namespace Trax.Dashboard.Components.Pages.Settings;

public partial class UserSettingsPage
{
    [Inject]
    private IDashboardSettingsService DashboardSettings { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    private int _pollingIntervalSeconds;
    private bool _hideAdminTrains;

    // Component visibility
    private bool _showServerHealth;
    private bool _showSummaryCards;
    private bool _showExecutionsChart;
    private bool _showStatusBreakdown;
    private bool _showTopFailures;
    private bool _showAvgDuration;
    private bool _showRecentFailures;
    private bool _showActiveManifests;
    private bool _showRealTimeMetrics;
    private bool _showThroughputChart;

    // Saved-state snapshots for dirty tracking
    private int _savedPollingIntervalSeconds;
    private bool _savedHideAdminTrains;
    private bool _savedShowServerHealth;
    private bool _savedShowSummaryCards;
    private bool _savedShowExecutionsChart;
    private bool _savedShowStatusBreakdown;
    private bool _savedShowTopFailures;
    private bool _savedShowAvgDuration;
    private bool _savedShowRecentFailures;
    private bool _savedShowActiveManifests;
    private bool _savedShowRealTimeMetrics;
    private bool _savedShowThroughputChart;

    private bool IsDataRefreshDirty => _pollingIntervalSeconds != _savedPollingIntervalSeconds;

    private bool IsAdminTrainsDirty => _hideAdminTrains != _savedHideAdminTrains;

    private bool IsComponentsDirty =>
        _showServerHealth != _savedShowServerHealth
        || _showSummaryCards != _savedShowSummaryCards
        || _showExecutionsChart != _savedShowExecutionsChart
        || _showStatusBreakdown != _savedShowStatusBreakdown
        || _showTopFailures != _savedShowTopFailures
        || _showAvgDuration != _savedShowAvgDuration
        || _showRecentFailures != _savedShowRecentFailures
        || _showActiveManifests != _savedShowActiveManifests
        || _showRealTimeMetrics != _savedShowRealTimeMetrics
        || _showThroughputChart != _savedShowThroughputChart;

    protected override async Task OnInitializedAsync()
    {
        await DashboardSettings.InitializeAsync();
        _pollingIntervalSeconds = (int)DashboardSettings.PollingInterval.TotalSeconds;
        _hideAdminTrains = DashboardSettings.HideAdminTrains;

        _showServerHealth = DashboardSettings.ShowServerHealth;
        _showSummaryCards = DashboardSettings.ShowSummaryCards;
        _showExecutionsChart = DashboardSettings.ShowExecutionsChart;
        _showStatusBreakdown = DashboardSettings.ShowStatusBreakdown;
        _showTopFailures = DashboardSettings.ShowTopFailures;
        _showAvgDuration = DashboardSettings.ShowAvgDuration;
        _showRecentFailures = DashboardSettings.ShowRecentFailures;
        _showActiveManifests = DashboardSettings.ShowActiveManifests;
        _showRealTimeMetrics = DashboardSettings.ShowRealTimeMetrics;
        _showThroughputChart = DashboardSettings.ShowThroughputChart;

        SnapshotSavedState();
    }

    private async Task Save()
    {
        await DashboardSettings.SetPollingIntervalAsync(_pollingIntervalSeconds);
        await DashboardSettings.SetHideAdminTrainsAsync(_hideAdminTrains);

        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowServerHealth,
            _showServerHealth
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowSummaryCards,
            _showSummaryCards
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowExecutionsChart,
            _showExecutionsChart
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowStatusBreakdown,
            _showStatusBreakdown
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowTopFailures,
            _showTopFailures
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowAvgDuration,
            _showAvgDuration
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowRecentFailures,
            _showRecentFailures
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowActiveManifests,
            _showActiveManifests
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowRealTimeMetrics,
            _showRealTimeMetrics
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowThroughputChart,
            _showThroughputChart
        );

        SnapshotSavedState();

        NotificationService.Notify(
            new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "Settings Saved",
                Detail = "User settings updated. Changes take effect on the next polling cycle.",
                Duration = 4000,
            }
        );
    }

    private async Task ResetDefault()
    {
        _pollingIntervalSeconds = DashboardSettingsService.DefaultPollingIntervalSeconds;
        _hideAdminTrains = DashboardSettingsService.DefaultHideAdminTrains;
        await DashboardSettings.SetPollingIntervalAsync(_pollingIntervalSeconds);
        await DashboardSettings.SetHideAdminTrainsAsync(_hideAdminTrains);

        _showServerHealth = DashboardSettingsService.DefaultComponentVisibility;
        _showSummaryCards = DashboardSettingsService.DefaultComponentVisibility;
        _showExecutionsChart = DashboardSettingsService.DefaultComponentVisibility;
        _showStatusBreakdown = DashboardSettingsService.DefaultComponentVisibility;
        _showTopFailures = DashboardSettingsService.DefaultComponentVisibility;
        _showAvgDuration = DashboardSettingsService.DefaultComponentVisibility;
        _showRecentFailures = DashboardSettingsService.DefaultComponentVisibility;
        _showActiveManifests = DashboardSettingsService.DefaultComponentVisibility;
        _showRealTimeMetrics = DashboardSettingsService.DefaultComponentVisibility;
        _showThroughputChart = DashboardSettingsService.DefaultComponentVisibility;

        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowServerHealth,
            _showServerHealth
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowSummaryCards,
            _showSummaryCards
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowExecutionsChart,
            _showExecutionsChart
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowStatusBreakdown,
            _showStatusBreakdown
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowTopFailures,
            _showTopFailures
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowAvgDuration,
            _showAvgDuration
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowRecentFailures,
            _showRecentFailures
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowActiveManifests,
            _showActiveManifests
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowRealTimeMetrics,
            _showRealTimeMetrics
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowThroughputChart,
            _showThroughputChart
        );

        SnapshotSavedState();

        NotificationService.Notify(
            new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "Default Restored",
                Detail = "All user settings have been reset to their default values.",
                Duration = 4000,
            }
        );
    }

    private void SnapshotSavedState()
    {
        _savedPollingIntervalSeconds = _pollingIntervalSeconds;
        _savedHideAdminTrains = _hideAdminTrains;
        _savedShowServerHealth = _showServerHealth;
        _savedShowSummaryCards = _showSummaryCards;
        _savedShowExecutionsChart = _showExecutionsChart;
        _savedShowStatusBreakdown = _showStatusBreakdown;
        _savedShowTopFailures = _showTopFailures;
        _savedShowAvgDuration = _showAvgDuration;
        _savedShowRecentFailures = _showRecentFailures;
        _savedShowActiveManifests = _showActiveManifests;
        _savedShowRealTimeMetrics = _showRealTimeMetrics;
        _savedShowThroughputChart = _showThroughputChart;
    }
}
