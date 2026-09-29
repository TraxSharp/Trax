namespace Trax.Dashboard.Services.DashboardSettings;

/// <summary>
/// Per-browser dashboard preferences (polling interval, admin-train filtering and which home
/// page panels are shown) plus the outcome of the most recent data refresh.
/// </summary>
/// <remarks>
/// <see cref="Extensions.DashboardServiceExtensions.AddTraxDashboard(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{Configuration.DashboardOptions}?)"/>
/// registers <see cref="DashboardSettingsService"/> as a scoped service, so in Blazor Server
/// there is one instance per circuit (browser tab). The default implementation persists every
/// preference in the browser's <c>localStorage</c>, not on the server. To replace it, register
/// your own implementation after <c>AddTraxDashboard</c>; the later registration wins.
/// Every component that reads these values calls <see cref="InitializeAsync"/> first, so an
/// implementation must make repeated calls cheap.
/// </remarks>
public interface IDashboardSettingsService
{
    /// <summary>
    /// How long the dashboard pages wait between data refreshes. Read on every poll tick, so a
    /// change takes effect on the next tick. Defaults to 5 seconds.
    /// </summary>
    TimeSpan PollingInterval { get; }

    /// <summary>
    /// UTC time of the most recent completed data load on any polling page in this circuit, as
    /// reported through <see cref="NotifyPolled"/>. The header uses it to draw the countdown to
    /// the next refresh.
    /// </summary>
    DateTime LastPollTime { get; }

    /// <summary>
    /// When <see langword="true"/>, the home page metrics, the trains list and the manifest and
    /// metadata lists leave out the scheduler's own administrative trains (manifest manager,
    /// job dispatcher, job runner and the cleanup trains), matched by their interface FullName.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    bool HideAdminTrains { get; }

    /// <summary>
    /// The short (unqualified) class names of the scheduler's administrative trains, for
    /// display. The dashboard does not filter by these: a short name can collide with a
    /// consumer's own train. Filters match a train's stored name, its interface FullName,
    /// against <c>Trax.Scheduler.Configuration.AdminTrains.FullNames</c>, as the API does.
    /// </summary>
    IReadOnlyList<string> AdminTrainNames { get; }

    /// <summary>
    /// Loads the stored preferences. Safe to call repeatedly: only the first call per instance
    /// reads storage. Until it has run, the other properties hold their defaults.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Sets <see cref="PollingInterval"/> and persists it.
    /// </summary>
    /// <param name="seconds">The interval in whole seconds. Values below 1 are raised to 1.</param>
    Task SetPollingIntervalAsync(int seconds);

    /// <summary>
    /// Sets <see cref="HideAdminTrains"/> and persists it.
    /// </summary>
    /// <param name="hide"><see langword="true"/> to hide administrative trains.</param>
    Task SetHideAdminTrainsAsync(bool hide);

    /// <summary>
    /// Records that a polling page has just finished loading its data: sets
    /// <see cref="LastPollTime"/> to the current UTC time and clears <see cref="LastPollError"/>.
    /// </summary>
    void NotifyPolled();

    /// <summary>
    /// The message of the most recent refresh that failed, or <c>null</c> once a refresh
    /// succeeds. While it is set, the data on screen is from the last refresh that worked.
    /// </summary>
    /// <remarks>
    /// Only background poll ticks report failures here; an exception from a page's first load
    /// or from an explicit refresh is not recorded. The interface's default implementation
    /// always returns <c>null</c>.
    /// </remarks>
    string? LastPollError => null;

    /// <summary>
    /// Records that a page's refresh failed. <see cref="NotifyPolled"/> clears it.
    /// </summary>
    /// <param name="message">The exception message, shown to the user as the reason.</param>
    /// <remarks>The interface's default implementation discards the message.</remarks>
    void NotifyPollFailed(string message) { }

    /// <summary>
    /// Whether the home page shows its summary (KPI) cards. Defaults to <see langword="true"/>.
    /// </summary>
    bool ShowSummaryCards { get; }

    /// <summary>
    /// Whether the home page shows the executions-over-time chart. Defaults to <see langword="true"/>.
    /// </summary>
    bool ShowExecutionsChart { get; }

    /// <summary>
    /// Whether the home page shows the top failing trains. Defaults to <see langword="true"/>.
    /// </summary>
    bool ShowFailures { get; }

    /// <summary>
    /// Whether the home page shows the average-duration panel. Defaults to <see langword="true"/>.
    /// </summary>
    bool ShowAvgDuration { get; }

    /// <summary>
    /// Whether the home page shows the server health panel (CPU, memory, GC heap and uptime of the
    /// process hosting the dashboard). Defaults to <see langword="true"/>.
    /// </summary>
    bool ShowServerHealth { get; }

    /// <summary>
    /// Shows or hides one home page panel and persists the choice.
    /// </summary>
    /// <param name="key">
    /// One of the visibility keys in <see cref="LocalStorage.StorageKeys"/>
    /// (<c>ShowSummaryCards</c>, <c>ShowExecutionsChart</c>, <c>ShowFailures</c>,
    /// <c>ShowAvgDuration</c>, <c>ShowServerHealth</c>). The default implementation still writes
    /// an unrecognised key to storage but changes no property.
    /// </param>
    /// <param name="visible"><see langword="true"/> to show the panel.</param>
    Task SetComponentVisibilityAsync(string key, bool visible);
}
