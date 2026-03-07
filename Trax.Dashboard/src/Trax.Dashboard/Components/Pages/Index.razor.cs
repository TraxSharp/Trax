using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Radzen;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Models;
using Trax.Dashboard.Utilities;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages;

public partial class Index
{
    private const string TimeRange1H = "1h";
    private const string TimeRange24H = "24h";

    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

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
    private List<TrainFailureCount> _topFailures = [];
    private List<TrainDuration> _avgDurations = [];

    // Throughput sparkline (7d) — one series per top train + "Other"
    private List<ThroughputSeries> _throughputSeries = [];

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

        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var todayStart = now.Date;

        var hideAdmin = DashboardSettings.HideAdminTrains;
        var adminNames = DashboardSettings.AdminTrainNames;

        // Summary cards — single GroupBy for today's state counts
        var todayQuery = context.Metadatas.AsNoTracking().Where(m => m.StartTime >= todayStart);

        if (hideAdmin)
            todayQuery = todayQuery.ExcludeAdmin(adminNames);

        var todayStateCounts = await todayQuery
            .GroupBy(m => m.TrainState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int CountForState(TrainState s) =>
            todayStateCounts.FirstOrDefault(x => x.State == s)?.Count ?? 0;

        _executionsToday = todayStateCounts.Sum(x => x.Count);

        var completed = CountForState(TrainState.Completed);
        var terminal = completed + CountForState(TrainState.Failed);
        _successRate = terminal > 0 ? Math.Round(100.0 * completed / terminal, 1) : 0;

        var runningQuery = context
            .Metadatas.AsNoTracking()
            .Where(m => m.TrainState == TrainState.InProgress);

        if (hideAdmin)
            runningQuery = runningQuery.ExcludeAdmin(adminNames);

        _currentlyRunning = await runningQuery.CountAsync(cancellationToken);

        _unresolvedDeadLetters = await context
            .DeadLetters.AsNoTracking()
            .CountAsync(d => d.Status == DeadLetterStatus.AwaitingIntervention, cancellationToken);

        // Executions over time (last 24h, grouped by hour)
        var last24h = now.AddHours(-24);
        var recentQuery = context.Metadatas.AsNoTracking().Where(m => m.StartTime >= last24h);

        if (hideAdmin)
            recentQuery = recentQuery.ExcludeAdmin(adminNames);

        var hourlyStats = await recentQuery
            .GroupBy(m => new
            {
                m.StartTime.Date,
                m.StartTime.Hour,
                m.TrainState,
            })
            .Select(g => new
            {
                g.Key.Date,
                g.Key.Hour,
                g.Key.TrainState,
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken);

        _hourlyData = Enumerable
            .Range(0, 24)
            .Select(i =>
            {
                var hourStart = now.AddHours(-23 + i);
                var targetDate = hourStart.Date;
                var targetHour = hourStart.Hour;
                return new ExecutionTimePoint
                {
                    Label = hourStart.ToString("HH"),
                    Completed = hourlyStats
                        .Where(x =>
                            x.Date == targetDate
                            && x.Hour == targetHour
                            && x.TrainState == TrainState.Completed
                        )
                        .Sum(x => x.Count),
                    Failed = hourlyStats
                        .Where(x =>
                            x.Date == targetDate
                            && x.Hour == targetHour
                            && x.TrainState == TrainState.Failed
                        )
                        .Sum(x => x.Count),
                    Cancelled = hourlyStats
                        .Where(x =>
                            x.Date == targetDate
                            && x.Hour == targetHour
                            && x.TrainState == TrainState.Cancelled
                        )
                        .Sum(x => x.Count),
                };
            })
            .ToList();

        // Per-minute data (last 60 minutes)
        var last60m = now.AddMinutes(-60);
        var minuteQuery = context.Metadatas.AsNoTracking().Where(m => m.StartTime >= last60m);

        if (hideAdmin)
            minuteQuery = minuteQuery.ExcludeAdmin(adminNames);

        var minuteStats = await minuteQuery
            .GroupBy(m => new
            {
                m.StartTime.Date,
                m.StartTime.Hour,
                m.StartTime.Minute,
                m.TrainState,
            })
            .Select(g => new
            {
                g.Key.Date,
                g.Key.Hour,
                g.Key.Minute,
                g.Key.TrainState,
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken);

        _minuteData = Enumerable
            .Range(0, 60)
            .Select(i =>
            {
                var minuteStart = now.AddMinutes(-59 + i);
                var targetDate = minuteStart.Date;
                var targetHour = minuteStart.Hour;
                var targetMinute = minuteStart.Minute;
                return new ExecutionTimePoint
                {
                    Label =
                        i % 5 == 0 ? minuteStart.ToString("HH:mm") : $"\u2009{minuteStart:HH:mm}",
                    Completed = minuteStats
                        .Where(x =>
                            x.Date == targetDate
                            && x.Hour == targetHour
                            && x.Minute == targetMinute
                            && x.TrainState == TrainState.Completed
                        )
                        .Sum(x => x.Count),
                    Failed = minuteStats
                        .Where(x =>
                            x.Date == targetDate
                            && x.Hour == targetHour
                            && x.Minute == targetMinute
                            && x.TrainState == TrainState.Failed
                        )
                        .Sum(x => x.Count),
                    Cancelled = minuteStats
                        .Where(x =>
                            x.Date == targetDate
                            && x.Hour == targetHour
                            && x.Minute == targetMinute
                            && x.TrainState == TrainState.Cancelled
                        )
                        .Sum(x => x.Count),
                };
            })
            .ToList();

        // Top failing trains (last 7 days)
        var last7d = now.AddDays(-7);
        var failuresQuery = context
            .Metadatas.AsNoTracking()
            .Where(m => m.TrainState == TrainState.Failed && m.StartTime >= last7d);

        if (hideAdmin)
            failuresQuery = failuresQuery.ExcludeAdmin(adminNames);

        _topFailures = (
            await failuresQuery
                .GroupBy(m => m.Name)
                .Select(g => new TrainFailureCount { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync(cancellationToken)
        )
            .Select(x => new TrainFailureCount { Name = ShortName(x.Name), Count = x.Count })
            .ToList();

        // Average duration by train (completed in last 7 days)
        var durationsQuery = context
            .Metadatas.AsNoTracking()
            .Where(m =>
                m.TrainState == TrainState.Completed
                && m.EndTime != null
                && m.StartTime >= last7d
                && m.ParentId == null
            );

        if (hideAdmin)
            durationsQuery = durationsQuery.ExcludeAdmin(adminNames);

        var avgDurationData = await durationsQuery
            .GroupBy(m => m.Name)
            .Select(g => new
            {
                Name = g.Key,
                AvgSeconds = g.Average(m => (m.EndTime!.Value - m.StartTime).TotalSeconds),
            })
            .OrderByDescending(x => x.AvgSeconds)
            .Take(10)
            .ToListAsync(cancellationToken);

        _avgDurations = avgDurationData
            .Select(x => new TrainDuration
            {
                Name = ShortName(x.Name),
                AvgMs = Math.Round(x.AvgSeconds * 1000, 0),
            })
            .ToList();

        // Throughput sparkline (completed per 6h block over 7d, by train)
        var throughputQuery = context
            .Metadatas.AsNoTracking()
            .Where(m => m.TrainState == TrainState.Completed && m.StartTime >= last7d);

        if (hideAdmin)
            throughputQuery = throughputQuery.ExcludeAdmin(adminNames);

        var throughputStats = await throughputQuery
            .GroupBy(m => new
            {
                m.StartTime.Date,
                Block = m.StartTime.Hour / 6,
                m.Name,
            })
            .Select(g => new
            {
                g.Key.Date,
                g.Key.Block,
                g.Key.Name,
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken);

        // Identify top 3 trains by total count
        var top3Names = throughputStats
            .GroupBy(x => x.Name)
            .OrderByDescending(g => g.Sum(x => x.Count))
            .Take(3)
            .Select(g => g.Key)
            .ToList();

        var top3Set = new HashSet<string>(top3Names);

        // Build time block labels
        var blockLabels = Enumerable
            .Range(0, 28)
            .Select(i =>
            {
                var blockStart = now.AddHours(-((27 - i) * 6));
                return new
                {
                    Date = blockStart.Date,
                    Block = blockStart.Hour / 6,
                    Label = blockStart.Hour == 0
                        ? blockStart.ToString("MMM dd")
                        : $"\u2009{blockStart:MMM dd HH}",
                };
            })
            .ToList();

        // Build one series per top train + "Other"
        var seriesNames = top3Names.Append("Other").ToList();
        string[] seriesColors = ["#2E7D32", "#1565C0", "#F9A825", "#78909C"];

        _throughputSeries = seriesNames
            .Select(
                (name, idx) =>
                    new ThroughputSeries
                    {
                        Name = name == "Other" ? "Other" : ShortName(name),
                        Color = seriesColors[idx],
                        Points = blockLabels
                            .Select(b => new ThroughputPoint
                            {
                                Label = b.Label,
                                Count =
                                    name == "Other"
                                        ? throughputStats
                                            .Where(x =>
                                                x.Date == b.Date
                                                && x.Block == b.Block
                                                && !top3Set.Contains(x.Name)
                                            )
                                            .Sum(x => x.Count)
                                        : throughputStats
                                            .Where(x =>
                                                x.Date == b.Date
                                                && x.Block == b.Block
                                                && x.Name == name
                                            )
                                            .Sum(x => x.Count),
                            })
                            .ToList(),
                    }
            )
            .Where(s => s.Points.Any(p => p.Count > 0))
            .ToList();
    }

    private string FormatCategoryLabel(object value)
    {
        var label = value?.ToString() ?? "";
        return label.StartsWith('\u2009') ? "" : label;
    }

    private void CollectServerHealthMetrics()
    {
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

        _memoryWorkingSetMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1);
        _gcHeapMb = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 1);

        _uptime = now - process.StartTime.ToUniversalTime();
    }
}
