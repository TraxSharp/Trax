using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Radzen;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Models;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages;

public partial class Index
{
    private const string TimeRange1H = "1h";
    private const string TimeRange24H = "24h";

    [Inject]
    private IOperationsService OperationsService { get; set; } = default!;

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    // KPI card values
    private int _executionsToday;
    private double _successRate;
    private int _currentlyRunning;
    private int _unresolvedDeadLetters;

    // Chart data
    private List<ExecutionTimePoint> _hourlyData = [];
    private List<ExecutionTimePoint> _minuteData = [];

    private List<ExecutionTimePoint> ExecutionsOverTime =>
        _selectedTimeRange == TimeRange1H ? _minuteData : _hourlyData;
    private string _selectedTimeRange = TimeRange24H;
    private List<Models.TrainFailureCount> _topFailures = [];
    private List<TrainDuration> _avgDurations = [];

    // Throughput sparkline (7d) — one series per top train + "Other"
    private static readonly string[] SeriesColors = { "#2E7D32", "#1565C0", "#F9A825", "#78909C" };
    private List<Models.ThroughputSeries> _throughputSeries = [];

    // Server health
    private double _cpuPercent;
    private double _memoryWorkingSetMb;
    private double _gcHeapMb;
    private TimeSpan _uptime;
    private TimeSpan _prevCpuTime;
    private DateTime _prevSampleTime = DateTime.UtcNow;

    protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        await DashboardSettings.InitializeAsync();

        CollectServerHealthMetrics();

        // The shared OperationsService is the single source of truth for these metrics;
        // the GraphQL operations.metrics.dashboard query reads the exact same data.
        var hideAdmin = DashboardSettings.HideAdminTrains;
        var hourly = await OperationsService.GetDashboardMetricsAsync(
            MetricsRange.Last24Hours,
            hideAdmin,
            cancellationToken
        );
        var minute = await OperationsService.GetDashboardMetricsAsync(
            MetricsRange.Last60Minutes,
            hideAdmin,
            cancellationToken
        );

        _executionsToday = hourly.Kpis.ExecutionsToday;
        _successRate = hourly.Kpis.SuccessRate;
        _currentlyRunning = hourly.Kpis.CurrentlyRunning;
        _unresolvedDeadLetters = hourly.Kpis.UnresolvedDeadLetters;

        _hourlyData = hourly
            .ExecutionsOverTime.Select(b => new ExecutionTimePoint
            {
                Label = b.Timestamp.ToString("HH"),
                Completed = b.Completed,
                Failed = b.Failed,
                Cancelled = b.Cancelled,
            })
            .ToList();

        _minuteData = minute
            .ExecutionsOverTime.Select(
                (b, i) =>
                    new ExecutionTimePoint
                    {
                        // Thin space prefix on non-5-minute marks suppresses the axis label
                        // while keeping the data point.
                        Label =
                            i % 5 == 0 ? b.Timestamp.ToString("HH:mm") : $" {b.Timestamp:HH:mm}",
                        Completed = b.Completed,
                        Failed = b.Failed,
                        Cancelled = b.Cancelled,
                    }
            )
            .ToList();

        _topFailures = hourly
            .TopFailures.Select(f => new Models.TrainFailureCount
            {
                Name = ShortName(f.TrainName),
                Count = f.Count,
            })
            .ToList();

        _avgDurations = hourly
            .TopAverageDurations.Select(d => new TrainDuration
            {
                Name = ShortName(d.TrainName),
                AvgMs = Math.Round(d.AverageMilliseconds, 0),
            })
            .ToList();

        _throughputSeries = hourly
            .ThroughputSeries.Select(
                (s, idx) =>
                    new Models.ThroughputSeries
                    {
                        Name = s.TrainName == "Other" ? "Other" : ShortName(s.TrainName),
                        Color = SeriesColors[Math.Min(idx, SeriesColors.Length - 1)],
                        Points = s
                            .Buckets.Select(b => new ThroughputPoint
                            {
                                Label =
                                    b.Timestamp.Hour == 0
                                        ? b.Timestamp.ToString("MMM dd")
                                        : $" {b.Timestamp:MMM dd HH}",
                                Count = b.Count,
                            })
                            .ToList(),
                    }
            )
            .ToList();
    }

    private string FormatCategoryLabel(object value)
    {
        var label = value?.ToString() ?? "";
        return label.StartsWith(' ') ? "" : label;
    }

    private void CollectServerHealthMetrics()
    {
        // Read memory / uptime through the shared service so the dashboard and the
        // GraphQL operations.metrics.server query report the same numbers.
        // CPU% requires per-instance sampling state and stays local to this component.
        var snap = OperationsService.GetServerMetrics();
        _memoryWorkingSetMb = Math.Round(snap.WorkingSetBytes / 1024.0 / 1024.0, 1);
        _gcHeapMb = Math.Round(snap.GcHeapBytes / 1024.0 / 1024.0, 1);
        _uptime = TimeSpan.FromSeconds(snap.UptimeSeconds);

        using var process = Process.GetCurrentProcess();
        var now = DateTime.UtcNow;
        var currentCpuTime = process.TotalProcessorTime;
        var elapsed = (now - _prevSampleTime).TotalMilliseconds;

        if (elapsed > 0 && _prevCpuTime != TimeSpan.Zero)
        {
            var cpuDelta = (currentCpuTime - _prevCpuTime).TotalMilliseconds;
            _cpuPercent = Math.Round(cpuDelta / elapsed / Environment.ProcessorCount * 100, 1);
            _cpuPercent = Math.Clamp(_cpuPercent, 0, 100);
        }

        _prevCpuTime = currentCpuTime;
        _prevSampleTime = now;
    }
}
