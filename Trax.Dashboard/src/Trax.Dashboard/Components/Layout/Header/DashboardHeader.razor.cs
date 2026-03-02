using Microsoft.AspNetCore.Components;
using Trax.Dashboard.Services.DashboardSettings;

namespace Trax.Dashboard.Components.Layout.Header;

public partial class DashboardHeader : IAsyncDisposable
{
    [Inject]
    private IDashboardSettingsService DashboardSettings { get; set; } = default!;

    [Parameter]
    public EventCallback OnToggleSidebar { get; set; }

    [Parameter]
    public EventCallback OnToggleTheme { get; set; }

    [Parameter]
    public bool IsDarkMode { get; set; }

    [Parameter]
    public string Title { get; set; } = "Trax Dashboard";

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

    public ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _uiTimer?.Dispose();
        _cts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
