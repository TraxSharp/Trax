using Microsoft.AspNetCore.Components;
using Trax.Dashboard.Services.DashboardSettings;

namespace Trax.Dashboard.Components.Layout.Header;

/// <summary>
/// The dashboard's top bar: title, environment badge, a "last refresh failed" marker, a
/// countdown to the next data refresh, the UTC clock, and the sidebar and theme toggles. It
/// redraws every 500 ms. Part of the dashboard UI; not intended to be used directly.
/// </summary>
public partial class DashboardHeader : IAsyncDisposable
{
    [Inject]
    private IDashboardSettingsService DashboardSettings { get; set; } = default!;

    /// <summary>Invoked when the user clicks the sidebar toggle.</summary>
    [Parameter]
    public EventCallback OnToggleSidebar { get; set; }

    /// <summary>Invoked when the user clicks the light/dark theme toggle.</summary>
    [Parameter]
    public EventCallback OnToggleTheme { get; set; }

    /// <summary>Whether a dark theme is active; picks the toggle's icon and tooltip.</summary>
    [Parameter]
    public bool IsDarkMode { get; set; }

    /// <summary>The heading text. The layout passes "<c>{DashboardOptions.Title} Dashboard</c>".</summary>
    [Parameter]
    public string Title { get; set; } = "Trax Dashboard";

    /// <summary>
    /// The host environment name shown in the badge: blue for <c>Development</c>, red for
    /// <c>Production</c>, grey otherwise.
    /// </summary>
    [Parameter]
    public string EnvironmentName { get; set; } = "";

    private string EnvironmentBadgeColor =>
        EnvironmentName.Equals("Development", StringComparison.OrdinalIgnoreCase) ? "#1976D2"
        : EnvironmentName.Equals("Production", StringComparison.OrdinalIgnoreCase) ? "#D32F2F"
        : "#757575";

    private PeriodicTimer? _uiTimer;
    private CancellationTokenSource? _cts;
    private int _secondsRemaining;
    private double _progressPercent;
    private string _utcTime = "";

    /// <summary>
    /// Initializes the dashboard settings and starts the 500 ms timer that redraws the countdown
    /// and clock.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await DashboardSettings.InitializeAsync();
        UpdateProgress();

        _cts = new CancellationTokenSource();
        _uiTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        _ = TickAsync(_cts.Token);
    }

    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            while (await _uiTimer!.WaitForNextTickAsync(ct))
            {
                UpdateProgress();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
            // disposed
        }
    }

    private void UpdateProgress()
    {
        var now = DateTime.UtcNow;

        _utcTime = now.ToString("yyyy-MM-dd HH:mm:ss");

        var elapsed = now - DashboardSettings.LastPollTime;
        var interval = DashboardSettings.PollingInterval;

        var ratio = interval.TotalSeconds > 0 ? elapsed.TotalSeconds / interval.TotalSeconds : 1.0;

        ratio = Math.Clamp(ratio, 0, 1);

        _progressPercent = ratio * 100;
        _secondsRemaining = Math.Max(
            0,
            (int)Math.Ceiling(interval.TotalSeconds - elapsed.TotalSeconds)
        );
    }

    /// <summary>Stops the redraw timer.</summary>
    public ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _uiTimer?.Dispose();
        _cts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
