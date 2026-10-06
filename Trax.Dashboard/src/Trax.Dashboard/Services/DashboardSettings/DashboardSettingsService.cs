using Trax.Dashboard.Services.LocalStorage;

namespace Trax.Dashboard.Services.DashboardSettings;

/// <summary>
/// Default <see cref="IDashboardSettingsService"/>: keeps each preference in memory for the
/// circuit and persists it to the browser's <c>localStorage</c> through
/// <see cref="ILocalStorageService"/>. Infrastructure registered by <c>AddTraxDashboard</c>;
/// not intended to be used directly.
/// </summary>
/// <param name="localStorage">Browser storage the preferences are read from and written to.</param>
internal class DashboardSettingsService(ILocalStorageService localStorage)
    : IDashboardSettingsService
{
    /// <summary>Polling interval, in seconds, used until the browser stores a different one.</summary>
    public const int DefaultPollingIntervalSeconds = 5;

    /// <summary>Administrative trains are hidden unless the browser stores otherwise.</summary>
    public const bool DefaultHideAdminTrains = true;

    /// <summary>Every home page panel is shown unless the browser stores otherwise.</summary>
    public const bool DefaultComponentVisibility = true;

    private bool _isInitialized;

    /// <inheritdoc/>
    public TimeSpan PollingInterval { get; private set; } =
        TimeSpan.FromSeconds(DefaultPollingIntervalSeconds);

    /// <inheritdoc/>
    /// <remarks>Starts at the time the service was constructed, before any poll has run.</remarks>
    public DateTime LastPollTime { get; private set; } = DateTime.UtcNow;

    /// <inheritdoc/>
    public string? LastPollError { get; private set; }

    /// <inheritdoc/>
    public bool HideAdminTrains { get; private set; } = DefaultHideAdminTrains;

    /// <inheritdoc/>
    public bool ShowSummaryCards { get; private set; } = DefaultComponentVisibility;

    /// <inheritdoc/>
    public bool ShowExecutionsChart { get; private set; } = DefaultComponentVisibility;

    /// <inheritdoc/>
    public bool ShowFailures { get; private set; } = DefaultComponentVisibility;

    /// <inheritdoc/>
    public bool ShowAvgDuration { get; private set; } = DefaultComponentVisibility;

    /// <inheritdoc/>
    public bool ShowServerHealth { get; private set; } = DefaultComponentVisibility;

    /// <inheritdoc/>
    /// <remarks>
    /// A stored polling interval of zero or less is ignored. When <c>localStorage</c> cannot be
    /// read (for example during prerendering) the defaults stay in place, and the instance still
    /// counts as initialized.
    /// </remarks>
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
        ShowFailures = await LoadVisibilityAsync(StorageKeys.ShowFailures);
        ShowAvgDuration = await LoadVisibilityAsync(StorageKeys.ShowAvgDuration);
        ShowServerHealth = await LoadVisibilityAsync(StorageKeys.ShowServerHealth);

        _isInitialized = true;
    }

    /// <inheritdoc/>
    public async Task SetPollingIntervalAsync(int seconds)
    {
        seconds = Math.Max(1, seconds);
        PollingInterval = TimeSpan.FromSeconds(seconds);
        await localStorage.SetAsync(StorageKeys.PollingInterval, seconds);
    }

    /// <inheritdoc/>
    public async Task SetHideAdminTrainsAsync(bool hide)
    {
        HideAdminTrains = hide;
        await localStorage.SetAsync(StorageKeys.HideAdminTrains, hide);
    }

    /// <inheritdoc/>
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
            case StorageKeys.ShowFailures:
                ShowFailures = visible;
                break;
            case StorageKeys.ShowAvgDuration:
                ShowAvgDuration = visible;
                break;
            case StorageKeys.ShowServerHealth:
                ShowServerHealth = visible;
                break;
        }

        await localStorage.SetAsync(key, visible);
    }

    /// <inheritdoc/>
    public void NotifyPolled()
    {
        LastPollTime = DateTime.UtcNow;
        LastPollError = null;
    }

    /// <inheritdoc/>
    public void NotifyPollFailed(string message) => LastPollError = message;

    private async Task<bool> LoadVisibilityAsync(string key)
    {
        var stored = await localStorage.GetAsync<bool?>(key);
        return stored ?? DefaultComponentVisibility;
    }
}
