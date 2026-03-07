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
    private bool _showFailures;
    private bool _showAvgDuration;

    // Saved-state snapshots for dirty tracking
    private int _savedPollingIntervalSeconds;
    private bool _savedHideAdminTrains;
    private bool _savedShowServerHealth;
    private bool _savedShowSummaryCards;
    private bool _savedShowExecutionsChart;
    private bool _savedShowFailures;
    private bool _savedShowAvgDuration;

    private bool IsDataRefreshDirty => _pollingIntervalSeconds != _savedPollingIntervalSeconds;

    private bool IsAdminTrainsDirty => _hideAdminTrains != _savedHideAdminTrains;

    private bool IsComponentsDirty =>
        _showServerHealth != _savedShowServerHealth
        || _showSummaryCards != _savedShowSummaryCards
        || _showExecutionsChart != _savedShowExecutionsChart
        || _showFailures != _savedShowFailures
        || _showAvgDuration != _savedShowAvgDuration;

    protected override async Task OnInitializedAsync()
    {
        await DashboardSettings.InitializeAsync();
        _pollingIntervalSeconds = (int)DashboardSettings.PollingInterval.TotalSeconds;
        _hideAdminTrains = DashboardSettings.HideAdminTrains;

        _showServerHealth = DashboardSettings.ShowServerHealth;
        _showSummaryCards = DashboardSettings.ShowSummaryCards;
        _showExecutionsChart = DashboardSettings.ShowExecutionsChart;
        _showFailures = DashboardSettings.ShowFailures;
        _showAvgDuration = DashboardSettings.ShowAvgDuration;

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
            StorageKeys.ShowFailures,
            _showFailures
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowAvgDuration,
            _showAvgDuration
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
        _showFailures = DashboardSettingsService.DefaultComponentVisibility;
        _showAvgDuration = DashboardSettingsService.DefaultComponentVisibility;

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
            StorageKeys.ShowFailures,
            _showFailures
        );
        await DashboardSettings.SetComponentVisibilityAsync(
            StorageKeys.ShowAvgDuration,
            _showAvgDuration
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
        _savedShowFailures = _showFailures;
        _savedShowAvgDuration = _showAvgDuration;
    }
}
